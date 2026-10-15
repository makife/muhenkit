// Gruplama (seçimi bloğa çevir), çözme (blok yerleştirmesini parçalarına ayır) ve
// blok yerleştirmesinin rengini değiştirme (bloğun yeniden renklendirilmiş kopyası).
#include <cmath>
#include <cstdio>
#include <set>

#include "document.h"

namespace mk {

namespace {

constexpr double kPi = 3.14159265358979323846;

std::string num(double v) {
  char b[40];
  std::snprintf(b, sizeof(b), "%.17g", v);
  return b;
}

/// Tanıtıcı/sahip/işaretçi ve genişletilmiş veri olmadan "kod\ndeğer\n…" (defineBlock girdisi)
void serialize(std::string& out, const std::string& type, const std::vector<Tag>& tags) {
  out += "0\n" + type + "\n";
  bool inGroup = false;
  for (const auto& t : tags) {
    if (t.code == 102) {
      inGroup = !inGroup && !t.value.empty() && t.value[0] == '{';
      continue;
    }
    if (inGroup || t.code == 5 || t.code == 105 || (t.code >= 330 && t.code <= 369) || t.code == 390 ||
        (t.code >= 1000 && t.code <= 1071))
      continue;
    out += std::to_string(t.code) + "\n" + t.value + "\n";
  }
}

void serialize(std::string& out, const Entity& e) {
  serialize(out, e.type, e.tags);
  for (const auto& c : e.children) serialize(out, c.type, c.tags);
  if (!e.seqend.empty() || e.type == "POLYLINE" || (e.type == "INSERT" && !e.children.empty()))
    serialize(out, "SEQEND", e.seqend.empty() ? std::vector<Tag>{{8, "0"}} : e.seqend);
}

std::string uniqueName(Document& doc, const std::string& base) {
  if (!doc.blocks.count(base)) return base;
  for (int i = 2;; i++) {
    std::string n = base + "_" + std::to_string(i);
    if (!doc.blocks.count(n)) return n;
  }
}

}  // namespace

std::string groupName(Document& doc) { return uniqueName(doc, "MK_GRUP_1"); }

bool makeGroupBlock(Document& doc, const std::vector<int>& ids, const std::string& name, double bx, double by,
                    std::string& error) {
  std::string snip;
  for (int id : ids) {
    Entity e = doc.entities[id];
    translateEntity(e, -bx, -by);
    serialize(snip, e);
  }
  return defineBlock(doc, name, snip, error);
}

bool recolorInsert(Document& doc, Entity& ins, int aci, std::string& error) {
  std::string name = ins.s(2);
  auto it = doc.blocks.find(name);
  if (it != doc.blocks.end()) {
    std::string clean;
    for (char ch : name) clean.push_back(ch == '*' || ch == '$' ? '_' : ch);
    std::string target = "MKC_" + clean + "_" + std::to_string(aci);
    if (!doc.blocks.count(target)) {
      std::string snip;
      for (Entity e : it->second.entities) {
        setEntityColor(e, aci == 256 ? 0 : aci);  // katman rengi: iç nesneler BLOKTAN → yerleştirmenin rengini alır
        for (auto& c : e.children) setEntityColor(c, aci == 256 ? 0 : aci);
        serialize(snip, e);
      }
      if (!defineBlock(doc, target, snip, error)) return false;
    }
    for (auto& t : ins.tags)
      if (t.code == 2) t.value = target;
  }
  setEntityColor(ins, aci);
  for (auto& c : ins.children) setEntityColor(c, aci);
  return true;
}

/// Yerleştirmenin dönüşümü: x' = T(ekleme)·R(açı)·S(sx,sy)·T(−taban)
static bool insertXf(const Document& doc, const Entity& ins, Xf& X, bool& uniform) {
  auto it = doc.blocks.find(ins.s(2));
  if (it == doc.blocks.end()) return false;
  double sx = ins.f(41, 1), sy = ins.f(42, 1), rot = ins.f(50, 0);
  double ix = ins.f(10), iy = ins.f(20), bx = it->second.bx, by = it->second.by;
  if (ins.flipped()) {  // OCS Z = −1: X aynası
    ix = -ix;
    sx = -sx;
    rot = 180 - rot;
  }
  uniform = std::fabs(std::fabs(sx) - std::fabs(sy)) < 1e-9 * std::max(1.0, std::fabs(sx));
  double r = rot * kPi / 180, cs = std::cos(r), sn = std::sin(r);
  X.a = cs * sx;
  X.b = sn * sx;
  X.c = -sn * sy;
  X.d = cs * sy;
  X.e = ix - (X.a * bx + X.c * by);
  X.f = iy - (X.b * bx + X.d * by);
  X.s = std::fabs(sx);
  if (sx * sy < 0) {
    X.mir = true;
    X.phi = sy < 0 ? rot / 2 : rot / 2 + 90;
  } else {
    X.mir = false;
    X.rot = sx < 0 ? rot + 180 : rot;
  }
  return true;
}

bool explodeInsert(Document& doc, const Entity& ins, std::vector<Entity>& out, std::string& error) {
  Xf X;
  bool uniform = true;
  if (!insertXf(doc, ins, X, uniform)) {
    error = "Blok tanımı bulunamadı.";
    return false;
  }
  const Block& blk = doc.blocks.at(ins.s(2));
  const std::string layer = ins.layer();
  const Tag* col = ins.find(62);
  // iç nesne katman 0 → yerleştirmenin katmanı; BLOKTAN renk → yerleştirmenin rengi (görünüm değişmesin)
  auto inherit = [&](Entity& e) {
    for (auto& t : e.tags) {
      if (t.code == 8 && t.value == "0") t.value = layer;
      if (t.code == 62 && t.i() == 0) t.value = col ? col->value : "256";
    }
  };
  if (!uniform) {
    // eşit olmayan ölçek: daire→elips olur; çizgilere dönüştür
    Document tmp;
    tmp.version = "AC1015";
    tmp.blocks[blk.name] = blk;
    tmp.entities.push_back(ins);
    tmp.entities.back().children.clear();
    buildScene(tmp);
    const float* v = tmp.vertices.data();
    for (const auto& b : tmp.batches)
      for (int64_t k = b.offset; k + 3 < b.offset + b.count; k += 4) {
        Entity l;
        l.type = "LINE";
        l.tags = {{100, "AcDbEntity"}, {8, layer}};
        char hex[16];
        std::snprintf(hex, sizeof(hex), "%u", b.rgb);
        l.tags.push_back({420, hex});
        l.tags.insert(l.tags.end(), {{100, "AcDbLine"}, {10, num(v[k])}, {20, num(v[k + 1])}, {30, "0.0"},
                                     {11, num(v[k + 2])}, {21, num(v[k + 3])}, {31, "0.0"}});
        out.push_back(std::move(l));
      }
  } else {
    for (const auto& src : blk.entities) {
      Entity e = src;
      if (!transformEntity(e, X)) continue;  // ölçü vb. desteklenmeyen türler atlanır
      inherit(e);
      for (auto& c : e.children) inherit(c);
      out.push_back(std::move(e));
    }
  }
  // öznitelikler (ATTRIB) → TEXT; gizliler atlanır
  for (const auto& a : ins.children) {
    if (a.type != "ATTRIB" || (a.n(70) & 1)) continue;
    Entity t;
    t.type = "TEXT";
    for (const auto& tg : a.tags) {
      if (tg.code == 2 || tg.code == 70 || tg.code == 73 || tg.code == 280 || tg.code == 5 || tg.code == 330) continue;
      if (tg.code == 100 && tg.value == "AcDbAttribute") continue;
      if (tg.code == 74) {
        t.tags.push_back({73, tg.value});  // ATTRIB dikey hiza 74 → TEXT 73
        continue;
      }
      t.tags.push_back(tg);
    }
    out.push_back(std::move(t));
  }
  if (out.empty()) {
    error = "Blok boş ya da parçalanamayan nesnelerden oluşuyor.";
    return false;
  }
  return true;
}

}  // namespace mk
