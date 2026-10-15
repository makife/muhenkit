// Sembol kütüphanesi üretici: mini DXF'ler → symbols.dat
#include <cstdio>
#include <fstream>
#include <iterator>
#include <map>
#include <set>
#include <string>
#include <vector>
#include "document.h"
#include "mkcad.h"
using namespace mk;
static std::string readStr(const std::string& p) { std::ifstream f(p, std::ios::binary); return {(std::istreambuf_iterator<char>(f)), {}}; }
static bool skipCode(int c) {
  // tanıtıcı/sahip/işaretçi ve genişletilmiş veri: hedef çizimde geçersiz olur
  return c == 5 || c == 105 || (c >= 330 && c <= 369) || c == 390 || (c >= 1000 && c <= 1071);
}
static void emit(std::string& out, const std::string& type, const std::vector<Tag>& tags, std::set<std::string>& types) {
  types.insert(type);
  // tek satır: kod \x1F değer \x1E — değerlerde denetim karakteri olmaz, "S" gibi yazılar karışmaz
  out += "0\x1f" + type + "\x1e";
  bool inGroup = false;
  for (const auto& t : tags) {
    if (t.code == 102) { inGroup = !inGroup && t.value.size() && t.value[0] == '{'; continue; }
    if (inGroup || skipCode(t.code)) continue;
    std::string v = t.value;
    if (type == "HATCH" && t.code == 97) v = "0";  // kaynak sınır nesnesi bağlantıları kaldırıldı
    out += std::to_string(t.code) + "\x1f" + v + "\x1e";
  }
}
int main(int argc, char** argv) {
  std::string dir = argv[1];
  // meta.json'dan basit okuma: "id": "...", "name": "...", "cat": "..."
  std::string meta = readStr(dir + "/meta.json");
  FILE* o = fopen(argv[2], "wb");
  auto field = [&](size_t& p, const char* key) {
    size_t k = meta.find(std::string("\"") + key + "\": ", p);
    if (k == std::string::npos) return std::string();
    k += std::string(key).size() + 4;
    if (meta[k] == '"') { size_t e = meta.find('"', k + 1); std::string s = meta.substr(k + 1, e - k - 1); p = e; return s; }
    size_t e = meta.find_first_of(",}", k); std::string s = meta.substr(k, e - k); p = e; return s;
  };
  size_t p = 0;
  std::string refh = field(p, "ref_h");
  fprintf(o, "#MKSYM 1\n#ref_h %s\n", refh.c_str());
  std::set<std::string> allTypes;
  int count = 0;
  while (true) {
    std::string id = field(p, "id");
    if (id.empty()) break;
    std::string name = field(p, "name"), cat = field(p, "cat"), w = field(p, "w"), h = field(p, "h");
    std::string src = readStr(dir + "/" + id + ".dxf");
    std::string err;
    auto d = readDxf((const uint8_t*)src.data(), src.size(), err);
    if (!d) { printf("HATA %s: %s\n", id.c_str(), err.c_str()); continue; }
    buildScene(*d);
    fprintf(o, "@SYM\t%s\t%s\t%s\t%s\t%s\n", id.c_str(), name.c_str(), cat.c_str(), w.c_str(), h.c_str());
    // önizleme: renkli çizgi partileri, dolgular, yazılar
    for (const auto& b : d->batches) {
      fprintf(o, "L\t%06X", b.rgb);
      for (int64_t k = b.offset; k < b.offset + b.count; k++) fprintf(o, " %.3f", d->vertices[k]);
      fprintf(o, "\n");
    }
    for (const auto& pl : d->polys) {
      fprintf(o, "F\t%06X\t%.2f", pl.rgb, pl.alpha);
      for (int k = 0; k < pl.npoints * 2; k++) fprintf(o, " %.3f", d->vertices[pl.offset + k]);
      fprintf(o, "\n");
    }
    for (const auto& t : d->texts) {
      std::string s = d->textPool.substr(t.utf8_offset, t.utf8_len);
      for (auto& ch : s) if (ch == '\n' || ch == '\t') ch = ' ';
      fprintf(o, "T\t%06X\t%.3f %.3f %.3f %.3f %d %d\t%s\n", t.rgb, t.x, t.y, t.height, t.rotation, t.halign, t.valign, s.c_str());
    }
    std::string snip;
    std::set<std::string> types;
    for (const auto& e : d->entities) {
      emit(snip, e.type, e.tags, types);
      for (const auto& c : e.children) emit(snip, c.type, c.tags, types);
      if (!e.seqend.empty() || e.type == "POLYLINE") emit(snip, "SEQEND", e.seqend, types);
    }
    allTypes.insert(types.begin(), types.end());
    fprintf(o, "S\t%s\n@END\n", snip.c_str());
    count++;
  }
  fclose(o);
  printf("sembol=%d turler:", count);
  for (auto& t : allTypes) printf(" %s", t.c_str());
  printf("\n");
}
