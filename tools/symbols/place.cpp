// symbols.dat içindeki tüm sembolleri hedef DXF'e tanımla ve ızgaraya yerleştir
#include <cstdio>
#include <fstream>
#include <iterator>
#include <sstream>
#include <string>
#include <vector>
#include "mkcad.h"
static std::string readStr(const char* p) { std::ifstream f(p, std::ios::binary); return {(std::istreambuf_iterator<char>(f)), {}}; }
int main(int argc, char** argv) {
  std::string src = readStr(argv[1]);
  MkDoc* d = mk_open((const uint8_t*)src.data(), src.size());
  std::istringstream lib(readStr(argv[2]));
  std::string line, id, snip;
  bool inSnip = false;
  int k = 0, ok = 0, empty = 0;
  while (std::getline(lib, line)) {
    if (line.rfind("@SYM\t", 0) == 0) { id = line.substr(5, line.find('\t', 5) - 5); continue; }
    if (line.rfind("S\t", 0) == 0) {
      snip.clear();
      for (size_t i = 2; i < line.size(); i++) snip += (line[i] == '\x1f' || line[i] == '\x1e') ? '\n' : line[i];
      continue;
    }
    if (line == "@END") {
      inSnip = false;
      std::string name = "MK_" + id;
      if (!mk_define_block(d, name.c_str(), snip.c_str())) { printf("TANIM HATASI %s: %s\n", id.c_str(), mk_last_error()); continue; }
      int e = mk_insert_block(d, "0", name.c_str(), (k % 10) * 120.0, -(k / 10) * 120.0, 1.0, 0);
      k++;
      if (e >= 0) {
        ok++;
        mk_build_scene(d);
        int64_t n = 0;
        mk_entity_geometry(d, e, &n);
        if (n == 0) { empty++; printf("BOS %s\n", id.c_str()); }
      }
      continue;
    }
    if (inSnip) snip += line + "\n";
  }
  int64_t len;
  const uint8_t* out = mk_save(d, &len);
  std::ofstream(argv[3], std::ios::binary).write((const char*)out, len);
  printf("yerlestirilen=%d bos=%d\n", ok, empty);
}
