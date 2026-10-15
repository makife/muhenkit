// Benzerlik dönüşümleri: döndür, ölçekle, ayna. Ayrıca yazı, katman ve renk değiştirme.
//
// Her nesne türünün etiketleri ayrı anlam taşır: noktalar, vektörler (yalnızca
// doğrusal kısım), uzunluklar (ölçekle çarpılır), açılar (döner/aynalanır) ve
// yay şişkinlikleri (aynada işaret değiştirir). OCS Z = -1 nesnelerde dönüşüm
// X aynasıyla eşlenerek nesnenin kendi düzleminde uygulanır.
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <set>

#include "document.h"

namespace mk {

namespace {

constexpr double kPi = 3.14159265358979323846;

std::string num(double v) {
  char buf[40];
  std::snprintf(buf, sizeof(buf), "%.17g", v);  // double kayıpsız gidiş-dönüş
  return buf;
}

bool isOcsType(const std::string& t) {
  static const std::set<std::string> s{"CIRCLE", "ARC", "LWPOLYLINE", "POLYLINE", "VERTEX", "TEXT", "ATTRIB",
                                       "INSERT", "SOLID", "TRACE", "HATCH"};
  return s.count(t) > 0;
}

}  // namespace

Xf makeRotate(double cx, double cy, double deg) {
  Xf x;
  double r = deg * kPi / 180;
  x.a = std::cos(r); x.b = std::sin(r); x.c = -std::sin(r); x.d = std::cos(r);
  x.e = cx - (x.a * cx + x.c * cy);
  x.f = cy - (x.b * cx + x.d * cy);
  x.rot = deg;
  return x;
}

Xf makeScale(double cx, double cy, double k) {
  Xf x;
  x.a = k; x.d = k;
  x.e = cx - k * cx;
  x.f = cy - k * cy;
  x.s = k;
  return x;
}

Xf makeMirror(double x1, double y1, double x2, double y2) {
  Xf x;
  double phi = std::atan2(y2 - y1, x2 - x1);
  x.a = std::cos(2 * phi); x.b = std::sin(2 * phi); x.c = std::sin(2 * phi); x.d = -std::cos(2 * phi);
  x.e = x1 - (x.a * x1 + x.c * y1);
  x.f = y1 - (x.b * x1 + x.d * y1);
  x.mir = true;
  x.phi = phi * 180 / kPi;
  return x;
}

namespace {

Tag* findIn(std::vector<Tag>& tags, int code) {
  for (auto& t : tags)
    if (t.code == code) return &t;
  return nullptr;
}

/// [code] etiketini ayarla; yoksa [after] kodlarından sonuncusunun ardına ekle.
void setTag(std::vector<Tag>& tags, int code, const std::string& value, std::initializer_list<int> after) {
  if (Tag* t = findIn(tags, code)) {
    t->value = value;
    return;
  }
  size_t pos = tags.size();
  for (size_t k = 0; k < tags.size(); k++)
    for (int a : after)
      if (tags[k].code == a) pos = k + 1;
  tags.insert(tags.begin() + static_cast<long>(pos), Tag{code, value});
}

void scaleTag(Tag& t, double s) { t.value = num(t.d() * s); }

void transformTags(const std::string& type, std::vector<Tag>& tags, const Xf& X) {
  // dönüşüm türüne göre etiket sınıfları
  std::set<int> vectors;
  if (type == "MTEXT" || type == "ELLIPSE" || type == "XLINE" || type == "RAY") vectors.insert(11);
  if (type == "SPLINE") vectors = {12, 13};
  std::set<int> lengths;
  if (type == "CIRCLE" || type == "ARC" || type == "TEXT" || type == "ATTRIB" || type == "ATTDEF") lengths = {40};
  if (type == "MTEXT") lengths = {40, 41, 46};
  if (type == "LWPOLYLINE") lengths = {40, 41, 43};
  if (type == "POLYLINE" || type == "VERTEX") lengths = {40, 41};
  if (type == "INSERT") lengths = {41, 42, 43, 44, 45};
  const bool hatch = type == "HATCH";
  const bool text = type == "TEXT" || type == "ATTRIB" || type == "ATTDEF" || type == "MTEXT";

  bool hatchBody = false;  // HATCH: "2" (desen adı) etiketinden önceki yükseklik noktası dönüştürülmez
  int lastEdge = 0;
  for (size_t k = 0; k < tags.size(); k++) {
    Tag& t = tags[k];
    int c = t.code;
    if (hatch && c == 2) hatchBody = true;
    if (hatch && c == 72) lastEdge = t.i();

    // ── noktalar ve vektörler
    if (c >= 10 && c <= 18) {
      if (hatch && !hatchBody) continue;
      for (size_t j = k + 1; j < tags.size() && j <= k + 3; j++) {
        if (tags[j].code != c + 10) continue;
        double x = t.d(), y = tags[j].d();
        bool isVec = vectors.count(c) || (hatch && c == 11 && lastEdge == 3);
        if (isVec) {
          if (type == "MTEXT" && X.mir) break;  // yazı okunur kalsın (AutoCAD MIRRTEXT=0)
          double len = std::hypot(x, y);
          X.vec(x, y);
          if (type == "MTEXT" || type == "SPLINE") {  // doğrultu vektörü: boyu korunur
            double nl = std::hypot(x, y);
            if (nl > 0) { x *= len / nl; y *= len / nl; }
          }
        } else {
          X.pt(x, y);
        }
        t.value = num(x);
        tags[j].value = num(y);
        break;
      }
      continue;
    }

    // ── uzunluklar
    if (lengths.count(c) && X.s != 1) {
      scaleTag(t, X.s);
      continue;
    }
    if (hatch && c == 40 && lastEdge == 2 && X.s != 1) {  // yay kenarı yarıçapı
      scaleTag(t, X.s);
      continue;
    }

    // ── açılar
    if (c == 50 || c == 51) {
      if (type == "ARC") {
        if (!X.mir) {
          if (X.rot != 0) t.value = num(t.d() + X.rot);
        } else if (c == 50) {
          // aynada yay yönü tersine döner: (s,e) → (2φ−e, 2φ−s)
          Tag* e = nullptr;
          for (size_t j = k + 1; j < tags.size(); j++)
            if (tags[j].code == 51) { e = &tags[j]; break; }
          if (e) {
            double s0 = t.d(), e0 = e->d();
            t.value = num(2 * X.phi - e0);
            e->value = num(2 * X.phi - s0);
            k = static_cast<size_t>(e - &tags[0]);
          }
        }
        continue;
      }
      if (hatch && (lastEdge == 2 || lastEdge == 3) && c == 50) {
        // kenar yönü 73 bayrağında (açılardan sonra gelir); saat yönündekilerde açılar ters işaretlidir
        Tag* e = nullptr;
        Tag* ccwTag = nullptr;
        for (size_t j = k + 1; j < tags.size() && j <= k + 4; j++) {
          if (tags[j].code == 51 && !e) e = &tags[j];
          if (tags[j].code == 73) { ccwTag = &tags[j]; break; }
        }
        if (!e) continue;
        bool ccw = !ccwTag || ccwTag->i() != 0;
        if (lastEdge == 3) {
          // elips kenarı: açılar ana eksene göredir (eksen zaten döner). Aynada yalnızca yön değişir.
          if (X.mir && ccwTag) ccwTag->value = ccw ? "0" : "1";
          continue;
        }
        double s0 = t.d(), e0 = e->d();
        if (!X.mir) {
          if (X.rot != 0) {
            t.value = num(s0 + (ccw ? X.rot : -X.rot));
            e->value = num(e0 + (ccw ? X.rot : -X.rot));
          }
        } else if (ccw) {
          // saat yönünün tersi (s→e) aynada saat yönüne döner: ters işaretli sakla
          t.value = num(s0 - 2 * X.phi);
          e->value = num(e0 - 2 * X.phi);
          if (ccwTag) ccwTag->value = "0";
        } else {
          t.value = num(2 * X.phi + s0);
          e->value = num(2 * X.phi + e0);
          if (ccwTag) ccwTag->value = "1";
        }
        continue;
      }
      if (c == 50 && (text || type == "INSERT")) {
        if (!X.mir) {
          if (X.rot != 0) t.value = num(t.d() + X.rot);
        } else if (type == "INSERT") t.value = num(2 * X.phi - t.d());
        // yazı aynada okunur kalır: açı değişmez
        continue;
      }
    }
    if (hatch && c == 52 && !X.mir && X.rot != 0) {  // desen açısı
      t.value = num(t.d() + X.rot);
      continue;
    }

    // ── elips parametreleri (radyan): aynada (s,e) → (−e, −s)
    if (type == "ELLIPSE" && c == 41 && X.mir) {
      Tag* e = nullptr;
      for (size_t j = k + 1; j < tags.size(); j++)
        if (tags[j].code == 42) { e = &tags[j]; break; }
      if (e) {
        double s0 = t.d(), e0 = e->d();
        bool full = std::fabs(e0 - s0 - 2 * kPi) < 1e-9;
        if (!full) {
          t.value = num(-e0);
          e->value = num(-s0);
        }
        k = static_cast<size_t>(e - &tags[0]);
      }
      continue;
    }

    // ── şişkinlikler: aynada yay yönü ters
    if (c == 42 && X.mir && (type == "LWPOLYLINE" || type == "VERTEX" || hatch)) {
      t.value = num(-t.d());
      continue;
    }
  }

  // blok ölçek etiketleri yoksa varsayılan 1'dir: ölçeklemede eklenmeli
  if (type == "INSERT" && X.s != 1) {
    if (!findIn(tags, 41)) setTag(tags, 41, num(X.s), {30, 20});
    if (!findIn(tags, 42)) setTag(tags, 42, num(X.s), {41});
  }

  // açı etiketi yoksa varsayılan 0'dır: döndürmede eklenmeli
  if (!X.mir && X.rot != 0 && !findIn(tags, 50)) {
    if (type == "TEXT" || type == "ATTRIB" || type == "ATTDEF") setTag(tags, 50, num(X.rot), {1, 40, 30});
    else if (type == "INSERT") setTag(tags, 50, num(X.rot), {43, 42, 41, 30});
    else if (type == "MTEXT" && !findIn(tags, 11)) setTag(tags, 50, num(X.rot), {7, 3, 1});
  }

  // blok aynası: R(2φ−θ)·S(sx, −sy)
  if (type == "INSERT" && X.mir) {
    Tag* sy = findIn(tags, 42);
    double v = sy ? sy->d() : 1.0;
    setTag(tags, 42, num(-v), {41, 30, 20});
    if (!findIn(tags, 50)) setTag(tags, 50, num(2 * X.phi), {43, 42, 41, 30});
  }
}

}  // namespace

bool transformEntity(Entity& e, const Xf& X) {
  static const std::set<std::string> unsupported{"DIMENSION", "VIEWPORT", "OLE2FRAME", "IMAGE", "MLINE", "LEADER",
                                                 "MULTILEADER", "TOLERANCE", "WIPEOUT", "REGION", "3DSOLID", "BODY"};
  if (unsupported.count(e.type)) return false;
  auto frame = [&](const Entity& ent, bool flipped) -> Xf {
    return (isOcsType(ent.type) && flipped) ? X.conj() : X;
  };
  transformTags(e.type, e.tags, frame(e, e.flipped()));
  for (auto& c : e.children) {
    bool fl = c.type == "VERTEX" ? e.flipped() : c.flipped();
    transformTags(c.type, c.tags, frame(c, fl));
  }
  return true;
}

// ───────────────────────── yazı, katman, renk ─────────────────────────

std::string entityText(const Entity& e) {
  if (e.type == "MTEXT") {
    std::string raw;
    for (const auto& t : e.tags)
      if (t.code == 3) raw += t.value;
    raw += e.s(1);
    return cleanMText(raw);
  }
  if (e.type == "TEXT" || e.type == "ATTRIB" || e.type == "ATTDEF") return cleanText(e.s(1));
  return "";
}

bool setEntityText(Entity& e, const std::string& utf8) {
  if (e.type == "MTEXT") {
    // satır sonları \P; 250 baytlık parçalar: 3 kodlu ara parçalar + son parça 1 kodunda
    std::string s;
    for (char ch : utf8) {
      if (ch == '\n') s += "\\P";
      else if (ch != '\r') s.push_back(ch);
    }
    std::vector<Tag> out;
    out.reserve(e.tags.size() + 2);
    size_t at = e.tags.size();
    for (size_t k = 0; k < e.tags.size(); k++) {
      if (e.tags[k].code == 3 || e.tags[k].code == 1) {
        if (at == e.tags.size()) at = out.size();
        continue;
      }
      out.push_back(e.tags[k]);
    }
    std::vector<Tag> chunks;
    size_t p = 0;
    while (s.size() - p > 250) {
      size_t n = 250;
      while (n > 0 && (static_cast<unsigned char>(s[p + n]) & 0xC0) == 0x80) n--;  // UTF-8 karakterini bölme
      chunks.push_back(Tag{3, s.substr(p, n)});
      p += n;
    }
    chunks.push_back(Tag{1, s.substr(p)});
    out.insert(out.begin() + static_cast<long>(std::min(at, out.size())), chunks.begin(), chunks.end());
    e.tags = std::move(out);
    return true;
  }
  if (e.type == "TEXT" || e.type == "ATTRIB" || e.type == "ATTDEF") {
    std::string s;
    for (char ch : utf8) {
      if (ch == '\n') s.push_back(' ');  // tek satırlı yazı: satır sonu boşluk olur
      else if (ch != '\r') s.push_back(ch);
    }
    setTag(e.tags, 1, s, {40, 30, 20});
    return true;
  }
  return false;
}

void setEntityLayer(Entity& e, const std::string& layer) { setTag(e.tags, 8, layer, {100, 330, 5}); }

void setEntityColor(Entity& e, int aci) {
  // gerçek renk (420) ACI'yi geçersiz kılar: kaldır
  e.tags.erase(std::remove_if(e.tags.begin(), e.tags.end(), [](const Tag& t) { return t.code == 420 || t.code == 430; }),
               e.tags.end());
  setTag(e.tags, 62, std::to_string(aci), {6, 8});
}

}  // namespace mk
