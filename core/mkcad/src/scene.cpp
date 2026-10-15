// Sahne üretimi: nesneleri dünya koordinatında çizgi parçaları, dolgu
// poligonları ve yazılara dönüştürür. Blokları matrislerle açar.
#include <algorithm>
#include <cmath>
#include <limits>
#include <map>
#include <utility>

#include "document.h"
#include "tables.h"

namespace mk {

namespace {

constexpr double kPi = 3.14159265358979323846;

// 2B afin matris: x' = a*x + c*y + e ; y' = b*x + d*y + f
struct M {
  double a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;
  void apply(double x, double y, double& ox, double& oy) const {
    ox = a * x + c * y + e;
    oy = b * x + d * y + f;
  }
  void vec(double x, double y, double& ox, double& oy) const {
    ox = a * x + c * y;
    oy = b * x + d * y;
  }
  M operator*(const M& o) const {  // this ∘ o
    M r;
    r.a = a * o.a + c * o.b;
    r.b = b * o.a + d * o.b;
    r.c = a * o.c + c * o.d;
    r.d = b * o.c + d * o.d;
    r.e = a * o.e + c * o.f + e;
    r.f = b * o.e + d * o.f + f;
    return r;
  }
  static M translate(double x, double y) { M m; m.e = x; m.f = y; return m; }
  static M scale(double sx, double sy) { M m; m.a = sx; m.d = sy; return m; }
  static M rotate(double deg) {
    M m;
    double r = deg * kPi / 180.0;
    m.a = std::cos(r); m.b = std::sin(r);
    m.c = -std::sin(r); m.d = std::cos(r);
    return m;
  }
};

const M kFlip = M::scale(-1, 1);  // OCS Z = -1 → X aynası

struct Ctx {
  M m;
  int parentLayer = -1;      // blok içinde "0" katmanının devralacağı katman
  uint32_t parentRgb = 0xFFFFFF;
  int depth = 0;
  int owner = -1;  // üst düzey nesne indeksi
};

struct Pt { double x, y; };

class Builder {
 public:
  explicit Builder(Document& d) : doc_(d) {}

  /// Tek nesne kipi: yalnızca [only] indeksli nesneyi üret (seçim vurgusu).
  void runOne(int only, std::vector<float>& out) {
    minx_ = miny_ = std::numeric_limits<double>::max();
    maxx_ = maxy_ = std::numeric_limits<double>::lowest();
    single_ = true;
    Ctx root;
    root.owner = only;
    if (only >= 0 && only < static_cast<int>(doc_.entities.size()) && !doc_.entities[only].deleted)
      entity(doc_.entities[only], root);
    out.clear();
    for (auto& kv : segs_) out.insert(out.end(), kv.second.begin(), kv.second.end());
    // dolgu ve yazı kutularını çerçeve olarak ekle
    for (auto& p : pendingPolys_) {
      const auto& v = p.second;
      for (size_t i = 0; i + 3 < v.size(); i += 2) {
        out.insert(out.end(), {v[i], v[i + 1], v[i + 2], v[i + 3]});
      }
    }
    for (const auto& t : singleTexts_) out.insert(out.end(), t.begin(), t.end());
  }

  void run() {
    doc_.vertices.clear();
    doc_.segOwner.clear();
    doc_.batches.clear();
    doc_.polys.clear();
    doc_.polyOwner.clear();
    doc_.texts.clear();
    doc_.textOwner.clear();
    doc_.textPool.clear();
    for (auto& l : doc_.layers) l.count = 0;
    minx_ = miny_ = std::numeric_limits<double>::max();
    maxx_ = maxy_ = std::numeric_limits<double>::lowest();

    for (size_t i = 0; i < doc_.entities.size(); i++) {
      if (doc_.entities[i].deleted) continue;
      Ctx root;
      root.owner = static_cast<int>(i);
      entity(doc_.entities[i], root);
    }

    // çizgi tamponlarını (katman, renk) gruplarına göre birleştir
    for (auto& kv : segs_) {
      auto& v = kv.second;
      if (v.empty()) continue;
      MkBatch b{};
      b.layer = kv.first.first;
      b.rgb = kv.first.second;
      b.kind = 0;
      b.offset = static_cast<int64_t>(doc_.vertices.size());
      b.count = static_cast<int64_t>(v.size());
      float bx0 = std::numeric_limits<float>::max(), by0 = bx0, bx1 = std::numeric_limits<float>::lowest(), by1 = bx1;
      for (size_t i = 0; i + 1 < v.size(); i += 2) {
        bx0 = std::min(bx0, v[i]); bx1 = std::max(bx1, v[i]);
        by0 = std::min(by0, v[i + 1]); by1 = std::max(by1, v[i + 1]);
      }
      b.minx = bx0; b.miny = by0; b.maxx = bx1; b.maxy = by1;
      doc_.vertices.insert(doc_.vertices.end(), v.begin(), v.end());
      auto& ow = owners_[kv.first];
      doc_.segOwner.insert(doc_.segOwner.end(), ow.begin(), ow.end());
      doc_.batches.push_back(b);
    }
    // dolgu poligonları
    for (auto& p : pendingPolys_) {
      p.first.offset = static_cast<int64_t>(doc_.vertices.size());
      doc_.vertices.insert(doc_.vertices.end(), p.second.begin(), p.second.end());
      doc_.polys.push_back(p.first);
    }
    doc_.polyOwner = pendingPolyOwner_;
    if (minx_ > maxx_) { minx_ = miny_ = 0; maxx_ = maxy_ = 1; }
    doc_.ext[0] = static_cast<float>(minx_);
    doc_.ext[1] = static_cast<float>(miny_);
    doc_.ext[2] = static_cast<float>(maxx_);
    doc_.ext[3] = static_cast<float>(maxy_);
  }

 private:
  Document& doc_;
  std::map<std::pair<int, uint32_t>, std::vector<float>> segs_;
  std::map<std::pair<int, uint32_t>, std::vector<int32_t>> owners_;
  std::vector<int32_t> pendingPolyOwner_;
  std::vector<std::vector<float>> singleTexts_;
  bool single_ = false;
  int curOwner_ = -1;
  std::vector<std::pair<MkPoly, std::vector<float>>> pendingPolys_;
  int polyGroup_ = 0;
  double minx_, miny_, maxx_, maxy_;
  // ~8M parça ≈ 128 MB köşe verisi; aşılırsa kalan parçalar çizilmez
  static constexpr size_t kMaxSegs = 8'000'000;
  size_t segTotal_ = 0;

  void grow(double x, double y) {
    if (!std::isfinite(x) || !std::isfinite(y)) return;
    minx_ = std::min(minx_, x); maxx_ = std::max(maxx_, x);
    miny_ = std::min(miny_, y); maxy_ = std::max(maxy_, y);
  }

  // ── renk ve katman çözümü
  int resolveLayer(const Entity& e, const Ctx& ctx) {
    std::string ln = e.layer();
    if (ln == "0" && ctx.parentLayer >= 0) return ctx.parentLayer;
    return doc_.layerOf(ln);
  }
  uint32_t resolveRgb(const Entity& e, int layer, const Ctx& ctx) {
    if (const Tag* tc = e.find(420)) return static_cast<uint32_t>(tc->i()) & 0xFFFFFF;  // gerçek renk
    int c = e.color();
    if (c == 256) return kAciRgb[std::clamp(doc_.layers[layer].aci, 1, 255)];
    if (c == 0) return ctx.parentRgb;  // BYBLOCK
    c = std::abs(c);
    return kAciRgb[std::clamp(c, 1, 255)];
  }

  // ── çıktı ilkelleri
  void seg(int layer, uint32_t rgb, const M& m, double x1, double y1, double x2, double y2) {
    if (++segTotal_ > kMaxSegs) return;  // telefonda bellek taşmasın
    double ax, ay, bx, by;
    m.apply(x1, y1, ax, ay);
    m.apply(x2, y2, bx, by);
    if (!std::isfinite(ax) || !std::isfinite(bx) || !std::isfinite(ay) || !std::isfinite(by)) return;
    auto& v = segs_[{layer, rgb}];
    v.push_back(static_cast<float>(ax)); v.push_back(static_cast<float>(ay));
    v.push_back(static_cast<float>(bx)); v.push_back(static_cast<float>(by));
    owners_[{layer, rgb}].push_back(curOwner_);
    grow(ax, ay);
    grow(bx, by);
  }

  void polyline(int layer, uint32_t rgb, const M& m, const std::vector<Pt>& p, bool closed) {
    for (size_t i = 0; i + 1 < p.size(); i++) seg(layer, rgb, m, p[i].x, p[i].y, p[i + 1].x, p[i + 1].y);
    if (closed && p.size() > 2) seg(layer, rgb, m, p.back().x, p.back().y, p[0].x, p[0].y);
  }

  void fill(int layer, uint32_t rgb, const M& m, const std::vector<std::vector<Pt>>& loops, float alpha) {
    int g = polyGroup_++;
    for (const auto& loop : loops) {
      if (loop.size() < 3) continue;
      MkPoly p{};
      p.layer = layer;
      p.rgb = rgb;
      p.group = g;
      p.npoints = static_cast<int32_t>(loop.size());
      p.alpha = alpha;
      std::vector<float> v;
      v.reserve(loop.size() * 2);
      for (const auto& q : loop) {
        double x, y;
        m.apply(q.x, q.y, x, y);
        v.push_back(static_cast<float>(x));
        v.push_back(static_cast<float>(y));
        grow(x, y);
      }
      pendingPolys_.emplace_back(p, std::move(v));
      pendingPolyOwner_.push_back(curOwner_);
    }
  }

  void text(int layer, uint32_t rgb, const M& m, double x, double y, double h, double rotDeg, double wf, int ha, int va,
            const std::string& utf8, double boxW) {
    if (utf8.empty() || h <= 0) return;
    double wx, wy;
    m.apply(x, y, wx, wy);
    double r = rotDeg * kPi / 180.0;
    double dx, dy, ux, uy;
    m.vec(std::cos(r), std::sin(r), dx, dy);
    m.vec(-std::sin(r), std::cos(r), ux, uy);
    double hs = std::hypot(ux, uy);
    MkText t{};
    t.x = static_cast<float>(wx);
    t.y = static_cast<float>(wy);
    t.height = static_cast<float>(h * hs);
    t.rotation = static_cast<float>(std::atan2(dy, dx) * 180.0 / kPi);
    t.width_factor = static_cast<float>(wf <= 0 ? 1 : wf);
    t.box_width = static_cast<float>(boxW * std::hypot(dx, dy));
    t.layer = layer;
    t.rgb = rgb;
    t.halign = ha;
    t.valign = va;
    t.utf8_offset = static_cast<int32_t>(doc_.textPool.size());
    t.utf8_len = static_cast<int32_t>(utf8.size());
    if (single_) {
      // yazı kutusunu yaklaşık çerçeve olarak ver
      double w = t.height * 0.62 * static_cast<double>(utf8.size()) * t.width_factor;
      double hh = t.height;
      double ox = ha == 1 ? -w / 2 : (ha == 2 ? -w : 0);
      double oy = va == 3 ? -hh : (va == 2 ? -hh / 2 : 0);
      double rr = t.rotation * kPi / 180.0, cs = std::cos(rr), sn = std::sin(rr);
      double cx[4] = {ox, ox + w, ox + w, ox}, cy[4] = {oy, oy, oy + hh, oy + hh};
      std::vector<float> box;
      for (int k = 0; k < 4; k++) {
        int j = (k + 1) % 4;
        box.push_back(static_cast<float>(wx + cx[k] * cs - cy[k] * sn));
        box.push_back(static_cast<float>(wy + cx[k] * sn + cy[k] * cs));
        box.push_back(static_cast<float>(wx + cx[j] * cs - cy[j] * sn));
        box.push_back(static_cast<float>(wy + cx[j] * sn + cy[j] * cs));
      }
      singleTexts_.push_back(std::move(box));
      return;
    }
    doc_.textPool += utf8;
    doc_.texts.push_back(t);
    doc_.textOwner.push_back(curOwner_);
    grow(wx, wy);
  }

  // ── geometri açıcılar

  /// s'den e'ye saat yönü tersine tarama, (0, period] aralığında. Farkı doğrudan alır: tam daire/elips
  /// (e = s + period) kayan nokta hatasıyla küçük bir yaya dönüşmesin; dev açı değerlerinde döngüye girmesin.
  static double sweepOf(double s, double e, double period) {
    double d = std::fmod(e - s, period);
    if (d <= 1e-9 * period) d += period;
    return d;
  }
  static std::vector<Pt> arcPts(double cx, double cy, double r, double s, double e) {
    if (!std::isfinite(s) || !std::isfinite(e) || !std::isfinite(r)) return {};
    double sweep = sweepOf(s, e, 360.0);
    s = std::fmod(s, 360.0);
    int n = std::clamp(static_cast<int>(std::ceil(sweep / 4.0)), 6, 180);
    std::vector<Pt> p;
    p.reserve(n + 1);
    for (int i = 0; i <= n; i++) {
      double a = (s + sweep * i / n) * kPi / 180.0;
      p.push_back({cx + r * std::cos(a), cy + r * std::sin(a)});
    }
    return p;
  }

  static std::vector<Pt> ellipsePts(double cx, double cy, double mx, double my, double ratio, double s, double e, double nz) {
    if (!std::isfinite(s) || !std::isfinite(e) || !std::isfinite(ratio)) return {};
    e = s + sweepOf(s, e, 2 * kPi);
    double minx = -my * ratio, miny = mx * ratio;  // Z × major
    if (nz < 0) { minx = -minx; miny = -miny; }
    int n = std::clamp(static_cast<int>(std::ceil((e - s) / (2 * kPi) * 96)), 8, 192);
    std::vector<Pt> p;
    p.reserve(n + 1);
    for (int i = 0; i <= n; i++) {
      double t = s + (e - s) * i / n;
      p.push_back({cx + mx * std::cos(t) + minx * std::sin(t), cy + my * std::cos(t) + miny * std::sin(t)});
    }
    return p;
  }

  static void bulgeSeg(std::vector<Pt>& out, Pt a, Pt b, double bulge) {
    if (std::fabs(bulge) < 1e-9) { out.push_back(b); return; }
    double theta = 4 * std::atan(bulge);
    double chord = std::hypot(b.x - a.x, b.y - a.y);
    if (chord < 1e-12) { out.push_back(b); return; }
    double r = chord / (2 * std::sin(theta / 2));
    double mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
    double h = std::sqrt(std::max(0.0, r * r - chord * chord / 4));
    double dx = (b.x - a.x) / chord, dy = (b.y - a.y) / chord;
    double nx = -dy, ny = dx;
    double sgn = (bulge > 0 ? 1 : -1) * (std::fabs(theta) > kPi ? -1 : 1);
    double cx = mx + nx * h * sgn, cy = my + ny * h * sgn;
    double sa = std::atan2(a.y - cy, a.x - cx);
    int segs = std::clamp(static_cast<int>(std::ceil(std::fabs(theta) / (2 * kPi) * 72)), 4, 72);
    for (int k = 1; k <= segs; k++) {
      double ang = sa + theta * k / segs;
      out.push_back({cx + std::fabs(r) * std::cos(ang), cy + std::fabs(r) * std::sin(ang)});
    }
  }

  static std::vector<Pt> bulgePolyline(const std::vector<Pt>& p, const std::vector<double>& bul, bool closed) {
    std::vector<Pt> out;
    if (p.empty()) return out;
    out.push_back(p[0]);
    size_t n = closed ? p.size() : p.size() - 1;
    for (size_t i = 0; i < n; i++) bulgeSeg(out, p[i], p[(i + 1) % p.size()], i < bul.size() ? bul[i] : 0);
    return out;
  }

  // De Boor ile B-spline değerlendirme (ağırlıklar yok sayılır)
  static std::vector<Pt> bspline(const std::vector<Pt>& ctrl, const std::vector<double>& knots, int p) {
    std::vector<Pt> out;
    // derece nokta sayısından küçük olmalı; yoksa de Boor penceresi dizinin dışına taşar
    if (ctrl.size() < 2 || p < 1 || p >= static_cast<int>(ctrl.size()) ||
        static_cast<int>(knots.size()) != static_cast<int>(ctrl.size()) + p + 1)
      return ctrl;
    double u0 = knots[p], u1 = knots[ctrl.size()];
    int n = std::max(16, static_cast<int>(ctrl.size()) * 12);
    for (int s = 0; s <= n; s++) {
      double u = u0 + (u1 - u0) * s / n;
      int k = p;
      while (k < static_cast<int>(ctrl.size()) - 1 && u >= knots[k + 1]) k++;
      std::vector<Pt> d(ctrl.begin() + (k - p), ctrl.begin() + k + 1);
      for (int r = 1; r <= p; r++) {
        for (int j = p; j >= r; j--) {
          double den = knots[j + 1 + k - r] - knots[j + k - p];
          double a = den == 0 ? 0 : (u - knots[j + k - p]) / den;
          d[j] = {(1 - a) * d[j - 1].x + a * d[j].x, (1 - a) * d[j - 1].y + a * d[j].y};
        }
      }
      out.push_back(d[p]);
    }
    return out;
  }

  // ── HATCH sınır yolları
  struct HatchCursor {
    const std::vector<Tag>& t;
    size_t k;
    double next(int code) {
      while (k < t.size() && t[k].code != code) k++;
      return k < t.size() ? t[k++].d() : 0;
    }
    int nextI(int code) { return static_cast<int>(std::lround(next(code))); }
    // dosyadan gelen sayaç: kalan etiket sayısından büyük olamaz (bozuk dosyada sonsuz döngü/bellek taşması olmasın)
    int count(int code) { return std::clamp(nextI(code), 0, static_cast<int>(t.size() - std::min(k, t.size()))); }
    bool done() const { return k >= t.size(); }
  };

  std::vector<std::vector<Pt>> hatchLoops(const Entity& e) {
    std::vector<std::vector<Pt>> loops;
    size_t start = 0;
    while (start < e.tags.size() && e.tags[start].code != 91) start++;
    if (start >= e.tags.size()) return loops;
    int nLoops = std::clamp(e.tags[start].i(), 0, static_cast<int>(e.tags.size()));
    HatchCursor c{e.tags, start + 1};
    for (int li = 0; li < nLoops && c.k < e.tags.size(); li++) {
      int type = c.nextI(92);
      std::vector<Pt> pts;
      if (type & 2) {
        bool hasBulge = c.nextI(72) != 0;
        c.nextI(73);
        int nv = c.count(93);
        std::vector<Pt> vs;
        std::vector<double> bs;
        for (int v = 0; v < nv && !c.done(); v++) {
          double x = c.next(10), y = c.next(20);
          vs.push_back({x, y});
          double b = 0;
          if (hasBulge && c.k < e.tags.size() && e.tags[c.k].code == 42) b = e.tags[c.k++].d();
          bs.push_back(b);
        }
        pts = bulgePolyline(vs, bs, true);
      } else {
        int ne = c.count(93);
        for (int ei = 0; ei < ne && !c.done(); ei++) {
          int et = c.nextI(72);
          if (et == 1) {
            double x1 = c.next(10), y1 = c.next(20), x2 = c.next(11), y2 = c.next(21);
            pts.push_back({x1, y1});
            pts.push_back({x2, y2});
          } else if (et == 2) {
            double cx = c.next(10), cy = c.next(20), r = c.next(40), s = c.next(50), en = c.next(51);
            bool ccw = c.nextI(73) != 0;
            // saat yönündeki kenarlarda açılar ters işaretli saklanır
            auto a = ccw ? arcPts(cx, cy, r, s, en) : arcPts(cx, cy, r, -en, -s);
            if (!ccw) std::reverse(a.begin(), a.end());
            pts.insert(pts.end(), a.begin(), a.end());
          } else if (et == 3) {
            double cx = c.next(10), cy = c.next(20), mx = c.next(11), my = c.next(21);
            double ratio = c.next(40), s = c.next(50) * kPi / 180, en = c.next(51) * kPi / 180;
            bool ccw = c.nextI(73) != 0;
            auto a = ccw ? ellipsePts(cx, cy, mx, my, ratio, s, en, 1) : ellipsePts(cx, cy, mx, my, ratio, -en, -s, 1);
            if (!ccw) std::reverse(a.begin(), a.end());
            pts.insert(pts.end(), a.begin(), a.end());
          } else if (et == 4) {
            int deg = c.nextI(94);
            c.nextI(73);
            c.nextI(74);
            int nk = c.count(95), nc = c.count(96);
            std::vector<double> knots;
            for (int q = 0; q < nk; q++) knots.push_back(c.next(40));
            std::vector<Pt> ctrl;
            for (int q = 0; q < nc; q++) {
              double x = c.next(10), y = c.next(20);
              ctrl.push_back({x, y});
            }
            auto a = bspline(ctrl, knots, deg);
            pts.insert(pts.end(), a.begin(), a.end());
          }
        }
      }
      int nsrc = c.count(97);
      for (int q = 0; q < nsrc; q++) c.next(330);
      if (pts.size() >= 3) loops.push_back(std::move(pts));
    }
    return loops;
  }

  // ── nesne yorumlayıcı
  void entity(const Entity& e, const Ctx& ctx) {
    curOwner_ = ctx.owner;
    const std::string& t = e.type;
    int layer = resolveLayer(e, ctx);
    uint32_t rgb = resolveRgb(e, layer, ctx);
    const M& m = ctx.m;
    M mo = e.flipped() ? m * kFlip : m;  // OCS nesneleri için
    if (!single_) doc_.layers[layer].count++;

    if (t == "LINE") {
      seg(layer, rgb, m, e.f(10), e.f(20), e.f(11), e.f(21));
    } else if (t == "LWPOLYLINE") {
      std::vector<Pt> p;
      std::vector<double> bul;
      for (size_t k = 0; k < e.tags.size(); k++) {
        const Tag& g = e.tags[k];
        if (g.code == 10) {
          double y = (k + 1 < e.tags.size() && e.tags[k + 1].code == 20) ? e.tags[k + 1].d() : 0;
          p.push_back({g.d(), y});
          bul.push_back(0);
        } else if (g.code == 42 && !bul.empty()) {
          bul.back() = g.d();
        }
      }
      bool closed = (e.n(70) & 1) != 0;
      polyline(layer, rgb, mo, bulgePolyline(p, bul, closed), false);
    } else if (t == "POLYLINE") {
      int fl = e.n(70);
      if (fl & (16 | 64)) {  // 3B ağ / çok yüzlü: kenarları düz çiz
        std::vector<Pt> p;
        for (const auto& v : e.children) p.push_back({v.f(10), v.f(20)});
        polyline(layer, rgb, m, p, false);
        return;
      }
      std::vector<Pt> p;
      std::vector<double> bul;
      for (const auto& v : e.children) {
        if (v.n(70) & 16) continue;  // spline çerçeve noktası
        p.push_back({v.f(10), v.f(20)});
        bul.push_back(v.f(42));
      }
      polyline(layer, rgb, (fl & 8) ? m : mo, bulgePolyline(p, bul, (fl & 1) != 0), false);
    } else if (t == "CIRCLE") {
      polyline(layer, rgb, mo, arcPts(e.f(10), e.f(20), e.f(40), 0, 360), false);
    } else if (t == "ARC") {
      polyline(layer, rgb, mo, arcPts(e.f(10), e.f(20), e.f(40), e.f(50), e.f(51)), false);
    } else if (t == "ELLIPSE") {
      polyline(layer, rgb, m,
               ellipsePts(e.f(10), e.f(20), e.f(11), e.f(21), e.f(40, 1), e.f(41), e.f(42, 2 * kPi), e.f(230, 1)), false);
    } else if (t == "TEXT" || t == "ATTRIB") {
      if (t == "ATTRIB" && (e.n(70) & 1)) return;  // gizli
      int ha = e.n(72), va = e.n(t == "TEXT" ? 73 : 74);
      bool useAlign = (ha != 0 || va != 0) && e.find(11);
      double x = useAlign ? e.f(11) : e.f(10), y = useAlign ? e.f(21) : e.f(20);
      if (ha == 3 || ha == 5) { ha = 0; x = e.f(10); y = e.f(20); }  // hizalı/sığdır → sol
      if (ha == 4) { ha = 1; va = 2; }                                // orta
      text(layer, rgb, mo, x, y, e.f(40, 2.5), e.f(50), e.f(41, 1), std::clamp(ha, 0, 2), std::clamp(va, 0, 3),
           cleanText(e.s(1)), 0);
    } else if (t == "MTEXT") {
      std::string raw;
      for (const auto& g : e.tags)
        if (g.code == 3) raw += g.value;
      raw += e.s(1);
      int att = std::clamp(e.n(71, 1), 1, 9);
      double rot = e.f(50);
      if (e.find(11)) rot = std::atan2(e.f(21), e.f(11)) * 180.0 / kPi;
      text(layer, rgb, m, e.f(10), e.f(20), e.f(40, 2.5), rot, 1, (att - 1) % 3, 3 - (att - 1) / 3, cleanMText(raw),
           e.f(41));
    } else if (t == "INSERT") {
      for (const auto& a : e.children) entity(a, ctx);  // öznitelikler dünya koordinatında
      auto it = doc_.blocks.find(e.s(2));
      if (it == doc_.blocks.end() || ctx.depth > 12 || segTotal_ > kMaxSegs) return;
      const Block& blk = it->second;
      int cols = std::clamp(e.n(70, 1), 1, 1000), rows = std::clamp(e.n(71, 1), 1, 1000);
      if (static_cast<long long>(cols) * rows > 10000) cols = rows = 1;  // bozuk/aşırı dizi
      double cs = e.f(44), rs = e.f(45);
      for (int r = 0; r < rows; r++) {
        for (int c = 0; c < cols; c++) {
          M local = M::translate(e.f(10), e.f(20)) * M::rotate(e.f(50)) * M::translate(c * cs, r * rs) *
                    M::scale(e.f(41, 1), e.f(42, 1)) * M::translate(-blk.bx, -blk.by);
          Ctx child;
          child.m = mo * local;
          child.parentLayer = layer;
          child.parentRgb = rgb;
          child.depth = ctx.depth + 1;
          child.owner = ctx.owner;
          for (const auto& be : blk.entities) entity(be, child);
        }
      }
    } else if (t == "DIMENSION") {
      auto it = doc_.blocks.find(e.s(2));
      if (it == doc_.blocks.end() || ctx.depth > 12) return;
      Ctx child = ctx;
      child.parentLayer = layer;
      child.parentRgb = rgb;
      child.depth = ctx.depth + 1;
      for (const auto& be : it->second.entities) entity(be, child);
    } else if (t == "SOLID" || t == "TRACE" || t == "3DFACE") {
      std::vector<Pt> q{{e.f(10), e.f(20)}, {e.f(11), e.f(21)}};
      bool four = e.find(13) != nullptr;
      if (t == "3DFACE") {
        q.push_back({e.f(12), e.f(22)});
        if (four) q.push_back({e.f(13), e.f(23)});
        polyline(layer, rgb, m, q, true);
        return;
      }
      // SOLID köşe sırası 1-2-4-3
      if (four) q.push_back({e.f(13), e.f(23)});
      q.push_back({e.f(12), e.f(22)});
      fill(layer, rgb, mo, {q}, 1.0f);
    } else if (t == "HATCH") {
      auto loops = hatchLoops(e);
      bool solid = e.n(70) == 1;
      fill(layer, rgb, mo, loops, solid ? 1.0f : 0.22f);
    } else if (t == "SPLINE") {
      std::vector<Pt> ctrl, fit;
      std::vector<double> knots;
      for (size_t k = 0; k < e.tags.size(); k++) {
        const Tag& g = e.tags[k];
        double y = (k + 1 < e.tags.size()) ? e.tags[k + 1].d() : 0;
        if (g.code == 10) ctrl.push_back({g.d(), y});
        if (g.code == 11) fit.push_back({g.d(), y});
        if (g.code == 40) knots.push_back(g.d());
      }
      auto p = ctrl.size() >= 2 ? bspline(ctrl, knots, e.n(71, 3)) : fit;
      polyline(layer, rgb, m, p, (e.n(70) & 1) != 0);
    } else {
      if (!single_) doc_.layers[layer].count--;  // çizilmeyen tür
    }
  }
};

}  // namespace

void buildScene(Document& doc) {
  Builder b(doc);
  b.run();
}

void entityGeometry(Document& doc, int idx, std::vector<float>& out) {
  Builder b(doc);
  b.runOne(idx, out);
}

}  // namespace mk
