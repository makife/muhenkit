// Kayıpsız DXF yazıcı: ENTITIES dışındaki bölümler orijinal etiketlerden aynen,
// ENTITIES ise güncel modelden yazılır. Değişmeyen nesnelerin değerleri
// (sayı biçimleri dahil) olduğu gibi korunur.
#include <cstdio>
#include <unordered_map>

#include "document.h"
#include "tables.h"

namespace mk {

namespace {

class Writer {
 public:
  explicit Writer(Document& d) : doc_(d) {
    int ver = d.versionNumber();
    utf8_ = ver >= 1021;
    if (!utf8_) {
      const uint16_t* table = kCp1252High;
      if (d.codepage.find("1251") != std::string::npos) table = kCp1251High;
      if (d.codepage.find("1254") != std::string::npos) table = kCp1254High;
      for (int b = 0; b < 128; b++) reverse_[table[b]] = static_cast<uint8_t>(0x80 + b);
    }
  }

  std::string run() {
    out_.reserve(4 << 20);
    for (const auto& sec : doc_.sections) {
      tag(0, "SECTION");
      tag(2, sec.name);
      if (sec.name == "ENTITIES") {
        for (const auto& e : doc_.entities)
          if (!e.deleted) entity(e);
      } else if (sec.name == "HEADER") {
        header(sec.tags);
      } else {
        for (const auto& t : sec.tags) tag(t.code, t.value);
      }
      tag(0, "ENDSEC");
    }
    // ENTITIES bölümü olmayan (bozuk) dosyaya da yeni nesneleri yaz
    bool hasEntities = false;
    for (const auto& s : doc_.sections) hasEntities |= s.name == "ENTITIES";
    if (!hasEntities) {
      tag(0, "SECTION");
      tag(2, "ENTITIES");
      for (const auto& e : doc_.entities)
        if (!e.deleted) entity(e);
      tag(0, "ENDSEC");
    }
    tag(0, "EOF");
    return out_;
  }

 private:
  Document& doc_;
  bool utf8_ = false;
  std::unordered_map<uint32_t, uint8_t> reverse_;
  std::string out_;

  void header(const std::vector<Tag>& tags) {
    for (size_t i = 0; i < tags.size(); i++) {
      tag(tags[i].code, tags[i].value);
      if (tags[i].code == 9 && tags[i].value == "$HANDSEED" && i + 1 < tags.size() &&
          doc_.maxHandle != doc_.loadedMaxHandle) {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "%llX", static_cast<unsigned long long>(doc_.maxHandle + 1));
        tag(tags[i + 1].code, buf);
        i++;
      }
    }
  }

  void entity(const Entity& e) {
    tag(0, e.type);
    for (const auto& t : e.tags) tag(t.code, t.value);
    for (const auto& c : e.children) {
      tag(0, c.type);
      for (const auto& t : c.tags) tag(t.code, t.value);
    }
    if (!e.seqend.empty() || (e.type == "POLYLINE") || (e.type == "INSERT" && !e.children.empty())) {
      tag(0, "SEQEND");
      for (const auto& t : e.seqend) tag(t.code, t.value);
    }
  }

  void tag(int code, const std::string& value) {
    char buf[16];
    std::snprintf(buf, sizeof(buf), "%3d\r\n", code);
    out_ += buf;
    encode(value);
    out_ += "\r\n";
  }

  // UTF-8 → dosya kod sayfası (2007 öncesi); karşılığı yoksa \U+XXXX
  void encode(const std::string& v) {
    if (utf8_) {
      out_ += v;
      return;
    }
    for (size_t i = 0; i < v.size();) {
      uint8_t c = static_cast<uint8_t>(v[i]);
      if (c < 0x80) {
        out_.push_back(static_cast<char>(c));
        i++;
        continue;
      }
      uint32_t cp = 0;
      int len = 1;
      if ((c & 0xE0) == 0xC0 && i + 1 < v.size()) {
        cp = ((c & 0x1F) << 6) | (v[i + 1] & 0x3F);
        len = 2;
      } else if ((c & 0xF0) == 0xE0 && i + 2 < v.size()) {
        cp = ((c & 0x0F) << 12) | ((v[i + 1] & 0x3F) << 6) | (v[i + 2] & 0x3F);
        len = 3;
      } else if ((c & 0xF8) == 0xF0 && i + 3 < v.size()) {
        cp = '?';
        len = 4;
      } else {
        cp = '?';
      }
      auto it = reverse_.find(cp);
      if (it != reverse_.end()) {
        out_.push_back(static_cast<char>(it->second));
      } else {
        char buf[16];
        std::snprintf(buf, sizeof(buf), "\\U+%04X", cp & 0xFFFF);
        out_ += buf;
      }
      i += len;
    }
  }
};

}  // namespace

std::string writeDxf(Document& doc) {
  Writer w(doc);
  return w.run();
}

}  // namespace mk
