// mkcad düzenleme ve kayıpsız kaydetme testi.
// kullanım: mkcad_edit_test girdi.dxf cikti_roundtrip.dxf cikti_duzenli.dxf
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#include "mkcad.h"

static int fails = 0;
#define CHECK(c, msg)                       \
  do {                                      \
    if (c) {                                \
      std::printf("  [OK]   %s\n", msg);    \
    } else {                                \
      std::printf("  [HATA] %s\n", msg);    \
      fails++;                              \
    }                                       \
  } while (0)

static void save(MkDoc* d, const char* path) {
  int64_t len = 0;
  const uint8_t* p = mk_save(d, &len);
  std::ofstream(path, std::ios::binary).write(reinterpret_cast<const char*>(p), len);
}

static std::string type(MkDoc* d, int id) {
  std::string s = mk_entity_info(d, id);
  return s.substr(0, s.find('|'));
}

int main(int argc, char** argv) {
  if (argc < 4) return 2;
  std::ifstream in(argv[1], std::ios::binary);
  std::vector<uint8_t> data((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
  MkDoc* d = mk_open(data.data(), static_cast<int64_t>(data.size()));
  if (!d) return 1;
  mk_build_scene(d);
  std::printf("1) Kayipsiz kaydetme\n");
  save(d, argv[2]);
  CHECK(mk_is_dirty(d) == 0, "kaydetme sonrasi kirli degil");

  std::printf("2) Secme ve yakalama\n");
  // ilk LINE nesnesini bul, ortasina dokun
  int line = -1;
  for (int i = 0; i < mk_entity_count(d); i++)
    if (type(d, i) == "LINE") { line = i; break; }
  int64_t n = 0;
  const float* g = mk_entity_geometry(d, line, &n);
  CHECK(n == 4, "LINE geometrisi 1 parca");
  double mx = (g[0] + g[2]) / 2, my = (g[1] + g[3]) / 2;
  CHECK(mk_pick(d, mx, my, 0.5) == line, "cizginin ortasina dokununca o cizgi secilir");
  double sp[2];
  int st = mk_snap(d, g[0] + 0.3, g[1] + 0.3, 2.0, sp);
  CHECK(st == 1 && std::abs(sp[0] - g[0]) < 1e-3 && std::abs(sp[1] - g[1]) < 1e-3, "uc noktaya yakalama");

  std::printf("3) Duzenleme + geri al\n");
  int before = mk_entity_count(d);
  int32_t ids[1] = {line};
  double ox = g[0], oy = g[1];
  CHECK(mk_move(d, ids, 1, 100, 50) == 1, "tasima");
  g = mk_entity_geometry(d, line, &n);
  CHECK(std::abs(g[0] - (ox + 100)) < 1e-3 && std::abs(g[1] - (oy + 50)) < 1e-3, "tasinan koordinat dogru");
  CHECK(mk_undo(d) == 1, "geri al");
  g = mk_entity_geometry(d, line, &n);
  CHECK(std::abs(g[0] - ox) < 1e-3, "geri alinca eski yer");
  CHECK(mk_redo(d) == 1, "yinele");
  g = mk_entity_geometry(d, line, &n);
  CHECK(std::abs(g[0] - (ox + 100)) < 1e-3, "yineleyince yeni yer");

  // bir blok (INSERT) kopyala
  int ins = -1;
  for (int i = 0; i < mk_entity_count(d); i++)
    if (type(d, i) == "INSERT") { ins = i; break; }
  int32_t ids2[1] = {ins};
  int copy = mk_copy(d, ids2, 1, 0, -200);
  CHECK(copy == before, "blok kopyasi yeni kimlik aldi");
  CHECK(mk_entity_count(d) == before + 1, "nesne sayisi +1");

  // bir yazi sil
  int txt = -1;
  for (int i = 0; i < before; i++)
    if (type(d, i) == "TEXT") { txt = i; break; }
  int32_t ids3[1] = {txt};
  CHECK(mk_delete(d, ids3, 1) == 1, "yazi silindi");
  CHECK(mk_pick(d, 1e9, 1e9, 1) == -1, "bos alana dokununca secim yok");

  int nl = mk_add_line(d, "0", 0, 0, 1000, 1000);
  int nt = mk_add_text(d, "0", 0, -100, 25, 0, "MühenKit ÇĞİÖŞÜ çğıöşü deneme");
  CHECK(nl > 0 && nt == nl + 1, "cizgi ve yazi eklendi");
  CHECK(mk_undo_count(d) == 5, "geri alma yigini 5 islem");
  CHECK(mk_undo(d) == 1 && mk_undo(d) == 1, "son iki ekleme geri alindi");
  CHECK(mk_redo(d) == 1 && mk_redo(d) == 1, "iki ekleme yinelendi");
  mk_build_scene(d);
  CHECK(mk_is_dirty(d) == 1, "belge kirli");
  save(d, argv[3]);
  mk_close(d);

  std::printf(fails ? "\nSONUC: %d HATA\n" : "\nSONUC: tum kontroller gecti\n", fails);
  return fails ? 1 : 0;
}
