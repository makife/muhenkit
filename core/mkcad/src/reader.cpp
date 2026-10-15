// ASCII DXF okuyucu (R12 – 2018). Bayt akışı → grup kodu çiftleri → model.
#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>

#include "document.h"
#include "tables.h"

namespace mk {

// ───────────────────────── yardımcılar ─────────────────────────

double Tag::d() const { return std::strtod(value.c_str(), nullptr); }
int Tag::i() const { return static_cast<int>(std::strtol(value.c_str(), nullptr, 10)); }

const Tag* Entity::find(int code) const {
  for (const auto& t : tags)
    if (t.code == code) return &t;
  return nullptr;
}
double Entity::f(int code, double def) const {
  const Tag* t = find(code);
  return t ? t->d() : def;
}
int Entity::n(int code, int def) const {
  const Tag* t = find(code);
  return t ? t->i() : def;
}
Tag* Entity::findMut(int code) {
  for (auto& t : tags)
    if (t.code == code) return &t;
  return nullptr;
}
std::string Entity::s(int code, const std::string& def) const {
  const Tag* t = find(code);
  return t ? t->value : def;
}

int Document::versionNumber() const {
  return version.size() > 2 ? std::atoi(version.c_str() + 2) : 0;
}

std::string Document::newHandle() {
  char buf[32];
  std::snprintf(buf, sizeof(buf), "%llX", static_cast<unsigned long long>(++maxHandle));
  return buf;
}

int Document::layerOf(const std::string& name) {
  auto it = layerIndex.find(name);
  if (it != layerIndex.end()) return it->second;
  Layer l;
  l.name = name;
  layers.push_back(l);
  int idx = static_cast<int>(layers.size()) - 1;
  layerIndex[name] = idx;
  return idx;
}

static void appendUtf8(std::string& out, uint32_t cp) {
  if (cp < 0x80) {
    out.push_back(static_cast<char>(cp));
  } else if (cp < 0x800) {
    out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
    out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
  } else {
    out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
    out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
  }
}

static std::string trim(const std::string& s) {
  size_t a = 0, b = s.size();
  while (a < b && (s[a] == ' ' || s[a] == '\t' || s[a] == '\r')) a++;
  while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t' || s[b - 1] == '\r')) b--;
  return s.substr(a, b - a);
}

// ───────────────────────── tokenizer ─────────────────────────

class Reader {
 public:
  Reader(const uint8_t* data, size_t len) : data_(data), len_(len) {}

  std::unique_ptr<Document> run(std::string& error) {
    if (len_ >= 18 && std::memcmp(data_, "AutoCAD Binary DXF", 18) == 0) {
      error = "Ikili (binary) DXF desteklenmiyor; ASCII DXF olarak kaydedin.";
      return nullptr;
    }
    detectEncoding();
    tokenize();
    if (tags_.empty()) {
      error = "Dosya bos ya da DXF degil.";
      return nullptr;
    }
    auto doc = std::make_unique<Document>();
    doc->version = version_;
    doc->codepage = codepage_;
    while (pos_ < tags_.size()) {
      const Tag& t = tags_[pos_];
      if (t.code == 0 && t.value == "SECTION") {
        pos_++;
        std::string name = pos_ < tags_.size() ? tags_[pos_].value : "";
        pos_++;
        // bölüm sonunu bul ve gövdeyi aynen sakla (ENTITIES yeniden üretilir)
        size_t start = pos_, end = pos_;
        while (end < tags_.size() && !(tags_[end].code == 0 && tags_[end].value == "ENDSEC")) end++;
        Section sec;
        sec.name = name;
        if (name != "ENTITIES") sec.tags.assign(tags_.begin() + start, tags_.begin() + end);
        doc->sections.push_back(std::move(sec));
        if (name == "TABLES") {
          parseTables(*doc);
        } else if (name == "BLOCKS") {
          parseBlocks(*doc);
        } else if (name == "ENTITIES") {
          parseEntities(doc->entities, "ENDSEC");
        }
        pos_ = end + 1;
      } else if (t.code == 0 && t.value == "EOF") {
        break;
      } else {
        pos_++;
      }
    }
    // en büyük handle ve model alanı sahibi
    for (const auto& tg : tags_) {
      if (tg.code == 5 || tg.code == 105) {
        uint64_t h = std::strtoull(tg.value.c_str(), nullptr, 16);
        if (h > doc->maxHandle) doc->maxHandle = h;
      }
    }
    doc->loadedMaxHandle = doc->maxHandle;
    for (const auto& e : doc->entities) {
      if (const Tag* o = e.find(330)) {
        doc->modelOwner = o->value;
        break;
      }
    }
    doc->layerOf("0");
    std::map<std::string, int> stats;
    for (const auto& e : doc->entities) stats[e.type]++;
    for (const auto& kv : stats) {
      if (!doc->typeStats.empty()) doc->typeStats += ";";
      doc->typeStats += kv.first + "=" + std::to_string(kv.second);
    }
    if (doc->entities.empty() && doc->blocks.empty()) {
      error = "Cizimde nesne bulunamadi.";
      return nullptr;
    }
    return doc;
  }

 private:
  const uint8_t* data_;
  size_t len_;
  std::string version_, codepage_;
  const uint16_t* table_ = nullptr;  // null → UTF-8
  std::vector<Tag> tags_;
  size_t pos_ = 0;

  // Başlıktan $ACADVER ve $DWGCODEPAGE değerlerini oku
  std::string headerValue(const char* key) {
    size_t lim = std::min<size_t>(len_, 60000);
    const char* p = reinterpret_cast<const char*>(data_);
    const char* hit = nullptr;
    size_t kl = std::strlen(key);
    for (size_t i = 0; i + kl < lim; i++) {
      if (std::memcmp(p + i, key, kl) == 0) {
        hit = p + i;
        break;
      }
    }
    if (!hit) return "";
    // anahtar satırından sonra 2 satır atla: grup kodu, değer
    const char* end = p + lim;
    const char* q = hit;
    int lines = 0;
    while (q < end && lines < 2) {
      if (*q == '\n') lines++;
      q++;
    }
    const char* e = q;
    while (e < end && *e != '\n' && *e != '\r') e++;
    return trim(std::string(q, e));
  }

  void detectEncoding() {
    version_ = headerValue("$ACADVER");
    codepage_ = headerValue("$DWGCODEPAGE");
    int ver = std::atoi(version_.c_str() + (version_.size() > 2 ? 2 : 0));
    if (ver >= 1021) {
      table_ = nullptr;  // 2007+ UTF-8
    } else if (codepage_.find("1251") != std::string::npos) {
      table_ = kCp1251High;
    } else if (codepage_.find("1254") != std::string::npos) {
      table_ = kCp1254High;
    } else {
      table_ = kCp1252High;
    }
  }

  std::string decode(const char* a, const char* b) {
    if (!table_) return std::string(a, b);
    std::string out;
    out.reserve(b - a);
    for (const char* c = a; c < b; c++) {
      uint8_t v = static_cast<uint8_t>(*c);
      if (v < 0x80) {
        out.push_back(static_cast<char>(v));
      } else {
        appendUtf8(out, table_[v - 0x80]);
      }
    }
    return out;
  }

  void tokenize() {
    const char* p = reinterpret_cast<const char*>(data_);
    const char* end = p + len_;
    // UTF-8 BOM
    if (len_ >= 3 && (uint8_t)p[0] == 0xEF && (uint8_t)p[1] == 0xBB && (uint8_t)p[2] == 0xBF) p += 3;
    tags_.reserve(len_ / 12);
    auto nextLine = [&](const char*& a, const char*& b) -> bool {
      if (p >= end) return false;
      a = p;
      while (p < end && *p != '\n') p++;
      b = p;
      if (b > a && *(b - 1) == '\r') b--;
      if (p < end) p++;
      return true;
    };
    const char *ca, *cb, *va, *vb;
    while (nextLine(ca, cb)) {
      // grup kodu satırı
      // strtol tampon sonunu bilmez: sayıyı satır sınırı içinde kendimiz oku
      while (ca < cb && (*ca == ' ' || *ca == '\t')) ca++;
      bool neg = ca < cb && *ca == '-';
      if (neg || (ca < cb && *ca == '+')) ca++;
      if (ca >= cb || *ca < '0' || *ca > '9') continue;  // hizasız satır: atla ve yeniden hizalan
      long code = 0;
      while (ca < cb && *ca >= '0' && *ca <= '9' && code < 100000) code = code * 10 + (*ca++ - '0');
      if (neg) code = -code;
      if (!nextLine(va, vb)) break;
      Tag t;
      t.code = static_cast<int>(code);
      t.value = decode(va, vb);
      if (t.code == 0 || t.code == 2 || t.code == 8 || t.code == 9) t.value = trim(t.value);
      tags_.push_back(std::move(t));
    }
  }

  void skipTo(const char* marker) {
    while (pos_ < tags_.size() && !(tags_[pos_].code == 0 && tags_[pos_].value == marker)) pos_++;
    if (pos_ < tags_.size()) pos_++;
  }

  // 0 kodlu bir sonraki etikete kadar (başlangıç dahil) grubu oku
  Entity readGroup() {
    Entity e;
    e.type = tags_[pos_].value;
    pos_++;
    while (pos_ < tags_.size() && tags_[pos_].code != 0) {
      e.tags.push_back(tags_[pos_]);
      pos_++;
    }
    return e;
  }

  bool at(const char* type) const { return pos_ < tags_.size() && tags_[pos_].code == 0 && tags_[pos_].value == type; }

  void parseTables(Document& doc) {
    while (pos_ < tags_.size() && !at("ENDSEC")) {
      if (at("LAYER")) {
        Entity g = readGroup();
        const Tag* nm = g.find(2);
        if (!nm) continue;
        int idx = doc.layerOf(nm->value);
        Layer& l = doc.layers[idx];
        int c = g.n(62, 7);
        l.aci = c == 0 ? 7 : std::abs(c);
        l.off = c < 0;
        l.frozen = (g.n(70) & 1) != 0;
      } else {
        pos_++;
      }
    }
    if (pos_ < tags_.size()) pos_++;
  }

  void parseBlocks(Document& doc) {
    while (pos_ < tags_.size() && !at("ENDSEC")) {
      if (at("BLOCK")) {
        Entity g = readGroup();
        Block b;
        b.name = trim(g.s(2));
        b.bx = g.f(10);
        b.by = g.f(20);
        parseEntities(b.entities, "ENDBLK");
        doc.blocks[b.name] = std::move(b);
      } else {
        pos_++;
      }
    }
    if (pos_ < tags_.size()) pos_++;
  }

  void parseEntities(std::vector<Entity>& out, const char* until) {
    while (pos_ < tags_.size()) {
      if (tags_[pos_].code != 0) {
        pos_++;
        continue;
      }
      if (at(until)) {
        readGroup();
        break;
      }
      if (at("ENDSEC")) break;
      Entity e = readGroup();
      if (e.type == "POLYLINE") {
        while (at("VERTEX")) e.children.push_back(readGroup());
        if (at("SEQEND")) e.seqend = readGroup().tags;
      } else if (e.type == "INSERT" && e.n(66) == 1) {
        while (at("ATTRIB")) e.children.push_back(readGroup());
        if (at("SEQEND")) e.seqend = readGroup().tags;
      } else if (e.type == "SEQEND" || e.type == "VERTEX") {
        continue;
      }
      out.push_back(std::move(e));
    }
  }
};

std::unique_ptr<Document> readDxf(const uint8_t* data, size_t len, std::string& error) {
  Reader r(data, len);
  return r.run(error);
}

}  // namespace mk
