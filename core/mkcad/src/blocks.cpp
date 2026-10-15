// Blok tanımı ekleme (sembol kütüphanesi) ve blok yerleştirme sayımı.
//
// Blok tanımı iki yere yazılır: BLOCKS bölümüne BLOCK…ENDBLK, R13+ dosyalarda ayrıca
// TABLES içindeki BLOCK_RECORD tablosuna bir kayıt. Bölümler ham etiket olarak
// saklandığından yazıcı onları değiştirmeden çıkarır.
#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <set>
#include <map>
#include <sstream>

#include "document.h"

namespace mk {

namespace {

/// "kod\ndeğer\n…" metnini nesnelere böl (0 kodu yeni nesne başlatır).
std::vector<Entity> parseSnippet(const std::string& s) {
  std::vector<Entity> out;
  std::istringstream in(s);
  std::string code, value;
  while (std::getline(in, code) && std::getline(in, value)) {
    if (!value.empty() && value.back() == '\r') value.pop_back();
    int c = std::atoi(code.c_str());
    if (c == 0) {
      out.emplace_back();
      out.back().type = value;
    } else if (!out.empty()) {
      out.back().tags.push_back(Tag{c, value});
    }
  }
  // POLYLINE→VERTEX…SEQEND ve INSERT→ATTRIB…SEQEND: alt nesne olarak bağla
  std::vector<Entity> folded;
  for (auto& e : out) {
    if (!folded.empty() && (e.type == "VERTEX" || e.type == "ATTRIB") &&
        (folded.back().type == "POLYLINE" || folded.back().type == "INSERT")) {
      folded.back().children.push_back(std::move(e));
    } else if (!folded.empty() && e.type == "SEQEND" && (folded.back().type == "POLYLINE" || folded.back().type == "INSERT")) {
      folded.back().seqend = std::move(e.tags);
      if (folded.back().seqend.empty()) folded.back().seqend.push_back({8, "0"});
    } else if (e.type != "SEQEND") {
      folded.push_back(std::move(e));
    }
  }
  return folded;
}

Section* findSection(Document& doc, const std::string& name) {
  for (auto& s : doc.sections)
    if (s.name == name) return &s;
  return nullptr;
}

/// TABLES içindeki bir tablonun kayıt adları (büyük harfe çevrilmiş): LTYPE, STYLE…
std::set<std::string> tableNames(Document& doc, const char* table) {
  std::set<std::string> out;
  Section* t = findSection(doc, "TABLES");
  if (!t) return out;
  auto& g = t->tags;
  for (size_t k = 0; k + 1 < g.size(); k++) {
    if (g[k].code == 0 && g[k].value == table) {
      for (size_t j = k + 1; j < g.size() && g[j].code != 0; j++)
        if (g[j].code == 2) {
          std::string n = g[j].value;
          for (auto& c : n) c = static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
          out.insert(n);
          break;
        }
    }
  }
  return out;
}

std::string upper(std::string s) {
  for (auto& c : s) c = static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
  return s;
}

/// Hedefte tanımlı olmayan çizgi tipi kaldırılır (sürekli çizilir), yazı stili STANDARD olur.
void sanitize(Document& doc, std::vector<Entity>& ents) {
  auto lt = tableNames(doc, "LTYPE");
  auto st = tableNames(doc, "STYLE");
  for (auto& e : ents) {
    std::vector<Tag> out;
    out.reserve(e.tags.size());
    for (auto& t : e.tags) {
      if (t.code == 6) {
        std::string u = upper(t.value);
        if (u != "BYLAYER" && u != "BYBLOCK" && !lt.count(u)) continue;
      }
      if (t.code == 7 && !st.count(upper(t.value))) t.value = st.count("STANDARD") ? "STANDARD" : (st.empty() ? t.value : *st.begin());
      out.push_back(t);
    }
    e.tags = std::move(out);
  }
}

/// R12'de olmayan türler (LWPOLYLINE, ELLIPSE, SPLINE, HATCH, MTEXT…) çizgi ve SOLID üçgenlere çevrilir.
void downgradeR12(std::vector<Entity>& ents) {
  static const std::set<std::string> ok{"LINE", "ARC", "CIRCLE", "TEXT", "SOLID", "POINT", "TRACE", "3DFACE",
                                        "POLYLINE", "VERTEX", "SEQEND"};
  Document tmp;
  tmp.version = "AC1015";
  tmp.entities = ents;
  buildScene(tmp);
  std::vector<Entity> out;
  auto num = [](double v) {
    char b[40];
    std::snprintf(b, sizeof(b), "%.10g", v);
    return std::string(b);
  };
  auto base = [&](const Entity& src, const char* type) {
    Entity e;
    e.type = type;
    e.tags.push_back({8, src.s(8, "0")});
    if (const Tag* c = src.find(62)) e.tags.push_back(*c);
    return e;
  };
  for (size_t i = 0; i < ents.size(); i++) {
    const Entity& src = ents[i];
    if (ok.count(src.type)) {
      out.push_back(src);
      continue;
    }
    if (src.type == "MTEXT") {  // tek satırlık TEXT'e
      Entity t = base(src, "TEXT");
      t.tags.insert(t.tags.end(), {{10, num(src.f(10))}, {20, num(src.f(20))}, {30, "0.0"}, {40, num(src.f(40, 2.5))},
                                   {1, cleanMText(src.s(1))}});
      out.push_back(std::move(t));
      continue;
    }
    const float* v = tmp.vertices.data();
    for (const auto& b : tmp.batches)
      for (int64_t k = b.offset; k + 3 < b.offset + b.count; k += 4) {
        if (tmp.segOwner[static_cast<size_t>(k / 4)] != static_cast<int>(i)) continue;
        Entity l = base(src, "LINE");
        l.tags.insert(l.tags.end(), {{10, num(v[k])}, {20, num(v[k + 1])}, {30, "0.0"},
                                     {11, num(v[k + 2])}, {21, num(v[k + 3])}, {31, "0.0"}});
        out.push_back(std::move(l));
      }
    for (size_t pi = 0; pi < tmp.polys.size(); pi++) {
      if (tmp.polyOwner[pi] != static_cast<int>(i)) continue;
      const auto& p = tmp.polys[pi];
      if (p.alpha < 0.99f) continue;  // desenli tarama: yalnız kenarlar
      // yelpaze üçgenleme (sembollerdeki dolgular dışbükey: daire, yarım daire)
      for (int j = 1; j + 1 < p.npoints; j++) {
        Entity t = base(src, "SOLID");
        const float* a = v + p.offset;
        double x[3] = {a[0], a[j * 2], a[(j + 1) * 2]}, y[3] = {a[1], a[j * 2 + 1], a[(j + 1) * 2 + 1]};
        t.tags.insert(t.tags.end(), {{10, num(x[0])}, {20, num(y[0])}, {30, "0.0"}, {11, num(x[1])}, {21, num(y[1])}, {31, "0.0"},
                                     {12, num(x[2])}, {22, num(y[2])}, {32, "0.0"}, {13, num(x[2])}, {23, num(y[2])}, {33, "0.0"}});
        out.push_back(std::move(t));
      }
    }
  }
  ents = std::move(out);
}

}  // namespace

bool defineBlock(Document& doc, const std::string& name, const std::string& snippet, std::string& error) {
  if (name.empty()) {
    error = "Blok adı boş.";
    return false;
  }
  if (doc.blocks.count(name)) return true;  // zaten tanımlı
  std::vector<Entity> ents = parseSnippet(snippet);
  if (ents.empty()) {
    error = "Blok içeriği boş.";
    return false;
  }
  const bool r13 = doc.versionNumber() >= 1012;
  const bool handles = r13 || doc.maxHandle > 0;
  if (!r13) downgradeR12(ents);
  sanitize(doc, ents);

  std::string recHandle;
  if (r13) {
    Section* tables = findSection(doc, "TABLES");
    if (!tables) {
      error = "Dosyada TABLES bölümü yok.";
      return false;
    }
    auto& t = tables->tags;
    size_t start = t.size();
    for (size_t k = 0; k + 1 < t.size(); k++) {
      if (t[k].code == 0 && t[k].value == "TABLE" && t[k + 1].code == 2 && t[k + 1].value == "BLOCK_RECORD") {
        start = k;
        break;
      }
    }
    if (start == t.size()) {
      error = "Dosyada BLOCK_RECORD tablosu yok.";
      return false;
    }
    std::string tableHandle;
    size_t k = start + 2;
    for (; k < t.size() && t[k].code != 0; k++) {
      if (t[k].code == 5) tableHandle = t[k].value;
      if (t[k].code == 70) t[k].value = std::to_string(t[k].i() + 1);  // kayıt sayısı
    }
    size_t end = k;
    while (end < t.size() && !(t[end].code == 0 && t[end].value == "ENDTAB")) end++;
    recHandle = doc.newHandle();
    std::vector<Tag> rec = {{0, "BLOCK_RECORD"},
                            {5, recHandle},
                            {330, tableHandle.empty() ? "0" : tableHandle},
                            {100, "AcDbSymbolTableRecord"},
                            {100, "AcDbBlockTableRecord"},
                            {2, name},
                            {340, "0"},
                            {70, "0"},
                            {280, "1"},
                            {281, "0"}};
    t.insert(t.begin() + static_cast<long>(end), rec.begin(), rec.end());
  }

  // nesne etiketleri: R13+ tanıtıcı ve sahip; R12'de alt sınıf işaretleri kaldırılır
  auto fix = [&](std::vector<Tag>& src, const std::string& owner) {
    std::vector<Tag> tags;
    std::string h;
    if (handles) {
      h = doc.newHandle();
      tags.push_back({5, h});
    }
    if (r13) tags.push_back({330, owner});
    for (auto& tg : src) {
      if (tg.code == 5 || tg.code == 330) continue;
      if (!r13 && tg.code == 100) continue;
      tags.push_back(tg);
    }
    src = std::move(tags);
    return h;
  };
  for (auto& e : ents) {
    std::string h = fix(e.tags, recHandle);
    for (auto& c : e.children) fix(c.tags, h.empty() ? recHandle : h);
    if (!e.seqend.empty()) fix(e.seqend, h.empty() ? recHandle : h);
  }

  Section* blocks = findSection(doc, "BLOCKS");
  if (!blocks) {
    // BLOCKS yoksa ENTITIES'ten önce oluştur
    Section s;
    s.name = "BLOCKS";
    auto it = doc.sections.begin();
    while (it != doc.sections.end() && it->name != "ENTITIES") ++it;
    blocks = &*doc.sections.insert(it, std::move(s));
  }
  auto& b = blocks->tags;
  b.push_back({0, "BLOCK"});
  if (handles) b.push_back({5, doc.newHandle()});
  if (r13) {
    b.push_back({330, recHandle});
    b.push_back({100, "AcDbEntity"});
  }
  b.push_back({8, "0"});
  if (r13) b.push_back({100, "AcDbBlockBegin"});
  b.insert(b.end(), {{2, name}, {70, "0"}, {10, "0.0"}, {20, "0.0"}, {30, "0.0"}, {3, name}});
  if (r13) b.push_back({1, ""});
  for (const auto& e : ents) {
    b.push_back({0, e.type});
    b.insert(b.end(), e.tags.begin(), e.tags.end());
    for (const auto& c : e.children) {
      b.push_back({0, c.type});
      b.insert(b.end(), c.tags.begin(), c.tags.end());
    }
    if (!e.seqend.empty()) {
      b.push_back({0, "SEQEND"});
      b.insert(b.end(), e.seqend.begin(), e.seqend.end());
    }
  }
  b.push_back({0, "ENDBLK"});
  if (handles) b.push_back({5, doc.newHandle()});
  if (r13) {
    b.push_back({330, recHandle});
    b.push_back({100, "AcDbEntity"});
  }
  b.push_back({8, "0"});
  if (r13) b.push_back({100, "AcDbBlockEnd"});

  // sahne için ayrıştırılmış tanım
  Block blk;
  blk.name = name;
  blk.entities = std::move(ents);
  doc.blocks[name] = std::move(blk);
  return true;
}

std::string insertCounts(const Document& doc) {
  std::map<std::string, int> n;
  for (const auto& e : doc.entities)
    if (!e.deleted && e.type == "INSERT") n[e.s(2)]++;
  std::string out;
  for (const auto& kv : n) {
    if (!out.empty()) out += '\n';
    out += kv.first + '\t' + std::to_string(kv.second);
  }
  return out;
}

}  // namespace mk
