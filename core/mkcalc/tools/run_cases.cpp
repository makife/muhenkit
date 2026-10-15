// cases.txt içindeki her durumu mc_calc ile hesaplar (ref.dart ile aynı biçimde yazar).
#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>

#include "mkcalc.h"

int main(int argc, char** argv) {
  std::ifstream f(argv[1], std::ios::binary);
  std::string all((std::istreambuf_iterator<char>(f)), {});
  size_t p = 0;
  while (p < all.size()) {
    size_t e = all.find("---\n", p);
    if (e == std::string::npos) e = all.size();
    std::string blk = all.substr(p, e - p);
    p = e + 4;
    if (blk.find("module=") == std::string::npos) continue;
    auto get = [&](const char* k) {
      std::string key = std::string(k) + "=";
      size_t i = blk.find(key);
      if (i == std::string::npos) return std::string();
      size_t j = blk.find('\n', i);
      return blk.substr(i + key.size(), j - i - key.size());
    };
    std::printf("case=%s\n%s---\n", get("case").c_str(), mc_calc(get("module").c_str(), blk.c_str()));
  }
}
