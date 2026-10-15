#include <algorithm>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iterator>
#include <set>
#include <sstream>
#include <string>
#include <vector>
#include "mkcad.h"
static std::string readStr(const char* p) { std::ifstream f(p, std::ios::binary); return {(std::istreambuf_iterator<char>(f)), {}}; }
static std::vector<float> geom(MkDoc* d, const std::vector<int>& ids) {
  std::vector<float> all;
  for (int id : ids) { int64_t n; const float* p = mk_entity_geometry(d, id, &n); all.insert(all.end(), p, p + n); }
  return all;
}
static double segD(double px, double py, const std::vector<float>& g) {
  double best = 1e300;
  for (size_t j = 0; j + 3 < g.size(); j += 4) {
    double ax = g[j], ay = g[j + 1], bx = g[j + 2], by = g[j + 3], vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
    double t = l2 > 0 ? ((px - ax) * vx + (py - ay) * vy) / l2 : 0;
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    best = std::min(best, std::hypot(px - (ax + t * vx), py - (ay + t * vy)));
  }
  return best;
}
static double diff(const std::vector<float>& a, const std::vector<float>& b) {
  double w = 0;
  for (size_t i = 0; i + 1 < a.size(); i += 2) w = std::max(w, segD(a[i], a[i + 1], b));
  for (size_t i = 0; i + 1 < b.size(); i += 2) w = std::max(w, segD(b[i], b[i + 1], a));
  return w;
}
int main(int argc, char** argv) {
  std::string src = readStr(argv[1]);
  MkDoc* d = mk_open((const uint8_t*)src.data(), src.size());
  mk_build_scene(d);
  // 1) mevcut tüm blok yerleştirmelerini çöz: görüntü aynı kalmalı
  int n0 = mk_entity_count(d), insOk = 0, insBad = 0;
  for (int i = 0; i < n0; i++) {
    if (std::string(mk_entity_info(d, i)).rfind("INSERT|", 0) != 0) continue;
    auto before = geom(d, {i});
    int cnt = 0;
    int first = mk_explode(d, &i, 1, &cnt);
    if (first < 0) { printf("cozulemedi #%d: %s\n", i, mk_last_error()); continue; }
    mk_build_scene(d);
    std::vector<int> ids; for (int k = 0; k < cnt; k++) ids.push_back(first + k);
    double e = diff(before, geom(d, ids));
    if (e < 0.05) insOk++; else { insBad++; printf("FARK #%d %.3f (%s)\n", i, e, mk_entity_info(d, i)); }
    mk_undo(d);
    mk_build_scene(d);
  }
  printf("dosyadaki bloklar: cozulen dogru=%d hatali=%d\n", insOk, insBad);
  // 2) grupla 40 nesne → görüntü aynı; çöz → aynı
  std::vector<int> pick;
  for (int i = 0; i < n0 && pick.size() < 40; i += 97) pick.push_back(i);
  auto g0 = geom(d, pick);
  int grp = mk_group(d, pick.data(), (int)pick.size());
  mk_build_scene(d);
  double eg = diff(g0, geom(d, {grp}));
  int cnt = 0;
  int first = mk_explode(d, &grp, 1, &cnt);
  mk_build_scene(d);
  std::vector<int> back; for (int k = 0; k < cnt; k++) back.push_back(first + k);
  double ee = diff(g0, geom(d, back));
  printf("grupla: %zu nesne -> blok #%d fark=%.4f | coz: %d nesne fark=%.4f\n", pick.size(), grp, eg, cnt, ee);
  // 3) sembol kütüphanesi: yerleştir, döndür+ölçekle, çöz → aynı; renk değiştir → tek renk
  std::istringstream lib(readStr(argv[2]));
  std::string line, id, snip;
  int sOk = 0, sBad = 0, cOk = 0, cBad = 0, k = 0;
  while (std::getline(lib, line)) {
    if (line.rfind("@SYM\t", 0) == 0) { id = line.substr(5, line.find('\t', 5) - 5); continue; }
    if (line.rfind("S\t", 0) == 0) { snip.clear(); for (size_t i = 2; i < line.size(); i++) snip += (line[i] == '\x1f' || line[i] == '\x1e') ? '\n' : line[i]; continue; }
    if (line != "@END") continue;
    std::string name = "MK_" + id;
    mk_define_block(d, name.c_str(), snip.c_str());
    int e = mk_insert_block(d, "0", name.c_str(), 9000 + (k % 10) * 150.0, (k / 10) * 150.0, 0.7, 30 * (k % 4));
    k++;
    mk_build_scene(d);
    auto before = geom(d, {e});
    int c = 0, f = mk_explode(d, &e, 1, &c);
    mk_build_scene(d);
    std::vector<int> ids; for (int q = 0; q < c; q++) ids.push_back(f + q);
    double er = diff(before, geom(d, ids));
    if (f >= 0 && er < 0.05) sOk++; else { sBad++; printf("sembol FARK %s %.3f\n", id.c_str(), er); }
    mk_undo(d);
    // renk: kırmızı (1) → yerleştirmenin tüm çizgileri kırmızı olmalı
    mk_set_color(d, &e, 1, 1);
    mk_build_scene(d);
    std::set<uint32_t> cols;
    const MkBatch* bt = mk_batches(d);
    // yerleştirmenin parçalarının rengini sahnenin çizgi partilerinden bul
    int64_t n; const float* g = mk_entity_geometry(d, e, &n);
    for (int b = 0; b < mk_batch_count(d); b++) {
      const float* v = mk_vertices(d) + bt[b].offset;
      for (int64_t q = 0; q + 3 < bt[b].count && n >= 4; q += 4)
        if (v[q] == g[0] && v[q + 1] == g[1] && v[q + 2] == g[2] && v[q + 3] == g[3]) cols.insert(bt[b].rgb);
    }
    // yalnız dolgudan oluşan semboller: dolgu renklerine bak
    if (cols.empty())
      for (int q = 0; q < mk_poly_count(d); q++) {
        const MkPoly& pp = mk_polys(d)[q];
        const float* v = mk_vertices(d) + pp.offset;
        for (int64_t z = 0; z + 1 < n; z += 2)
          if (v[0] == g[z] && v[1] == g[z + 1]) { cols.insert(pp.rgb); break; }
      }
    if (cols.size() == 1 && *cols.begin() == 0xFF0000) cOk++;
    else {
      cBad++;
      printf("renk %s: %zu renk, %lld nokta:", id.c_str(), cols.size(), (long long)n);
      for (auto c : cols) printf(" %06X", c);
      printf("\n");
    }
  }
  printf("semboller: cozulen dogru=%d hatali=%d | renk degisimi uygulanan=%d uygulanmayan=%d\n", sOk, sBad, cOk, cBad);
  int64_t len;
  const uint8_t* out = mk_save(d, &len);
  std::ofstream(argv[3], std::ios::binary).write((const char*)out, len);
}
