// mkcad komut satırı test aracı: DXF'i okur, istatistik yazar, isteğe bağlı
// olarak sahneyi basit bir ikili dosyaya döker (Python ile görselleştirme için).
#include <chrono>
#include <cstdio>
#include <fstream>
#include <iterator>
#include <vector>

#include "mkcad.h"

int main(int argc, char** argv) {
  if (argc < 2) {
    std::puts("kullanim: mkcad_cli dosya.dxf [sahne.bin]");
    return 2;
  }
  std::ifstream in(argv[1], std::ios::binary);
  std::vector<uint8_t> data((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
  auto t0 = std::chrono::steady_clock::now();
  MkDoc* d = mk_open(data.data(), static_cast<int64_t>(data.size()));
  if (!d) {
    std::printf("HATA: %s\n", mk_last_error());
    return 1;
  }
  auto t1 = std::chrono::steady_clock::now();
  mk_build_scene(d);
  auto t2 = std::chrono::steady_clock::now();
  float ext[4];
  mk_extents(d, ext);
  std::printf("surum=%s kodsayfasi=%s\n", mk_dxf_version(d), mk_codepage(d));
  std::printf("nesne=%d blok=%d katman=%d\n", mk_entity_count(d), mk_block_count(d), mk_layer_count(d));
  std::printf("turler=%s\n", mk_type_stats(d));
  std::printf("parca_float=%lld grup=%d dolgu=%d yazi=%d\n", (long long)mk_vertex_float_count(d), mk_batch_count(d),
              mk_poly_count(d), mk_text_count(d));
  std::printf("sinirlar=%.1f %.1f %.1f %.1f\n", ext[0], ext[1], ext[2], ext[3]);
  std::printf("sure_okuma_ms=%.1f sure_sahne_ms=%.1f\n",
              std::chrono::duration<double, std::milli>(t1 - t0).count(),
              std::chrono::duration<double, std::milli>(t2 - t1).count());
  if (argc >= 3) {
    std::ofstream o(argv[2], std::ios::binary);
    int32_t nb = mk_batch_count(d), np = mk_poly_count(d), nt = mk_text_count(d);
    int64_t nv = mk_vertex_float_count(d);
    o.write(reinterpret_cast<const char*>(&nv), 8);
    o.write(reinterpret_cast<const char*>(mk_vertices(d)), nv * 4);
    o.write(reinterpret_cast<const char*>(&nb), 4);
    o.write(reinterpret_cast<const char*>(mk_batches(d)), nb * sizeof(MkBatch));
    o.write(reinterpret_cast<const char*>(&np), 4);
    o.write(reinterpret_cast<const char*>(mk_polys(d)), np * sizeof(MkPoly));
    o.write(reinterpret_cast<const char*>(&nt), 4);
    o.write(reinterpret_cast<const char*>(mk_texts(d)), nt * sizeof(MkText));
    const char* pool = mk_text_pool(d);
    int64_t pl = 0;
    while (pool[pl]) pl++;
    o.write(reinterpret_cast<const char*>(&pl), 8);
    o.write(pool, pl);
    std::printf("sahne yazildi: %s\n", argv[2]);
  }
  mk_close(d);
  return 0;
}
