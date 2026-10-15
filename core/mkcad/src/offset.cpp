// Ötele (offset): nesnenin belirli mesafede paraleli. Taraf, dokunulan noktayla seçilir.
// Desteklenen: LINE, CIRCLE, ARC, düz parçalı LWPOLYLINE. OCS Z = −1 nesnelerde nokta
// nesnenin kendi düzlemine (X aynası) çevrilerek hesaplanır.
#include <cmath>
#include <cstdio>
#include <vector>

#include "document.h"

namespace mk {

namespace {

std::string num(double v) {
  char buf[40];
  std::snprintf(buf, sizeof(buf), "%.17g", v);
  return buf;
}

struct P {
  double x, y;
};

/// a→b doğrusunun soluna birim normal
P leftNormal(P a, P b) {
  double dx = b.x - a.x, dy = b.y - a.y, l = std::hypot(dx, dy);
  if (l < 1e-12) return {0, 0};
  return {-dy / l, dx / l};
}

double cross(P a, P b, P p) { return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x); }

double segDist(P p, P a, P b) {
  double vx = b.x - a.x, vy = b.y - a.y, l2 = vx * vx + vy * vy;
  double t = l2 > 0 ? ((p.x - a.x) * vx + (p.y - a.y) * vy) / l2 : 0;
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return std::hypot(p.x - (a.x + t * vx), p.y - (a.y + t * vy));
}

/// İki doğrunun (p1 + t·d1, p2 + s·d2) kesişimi; paralelse false
bool intersect(P p1, P d1, P p2, P d2, P& out) {
  double den = d1.x * d2.y - d1.y * d2.x;
  if (std::fabs(den) < 1e-12) return false;
  double t = ((p2.x - p1.x) * d2.y - (p2.y - p1.y) * d2.x) / den;
  out = {p1.x + t * d1.x, p1.y + t * d1.y};
  return true;
}

}  // namespace

bool offsetEntity(const Entity& src, double d, double px, double py, Entity& out) {
  if (!(d > 0)) return false;
  out = src;
  out.children.clear();
  out.seqend.clear();
  // OCS Z = −1: X aynalı düzlem
  if (src.flipped() && (src.type == "CIRCLE" || src.type == "ARC" || src.type == "LWPOLYLINE")) px = -px;
  P p{px, py};

  if (src.type == "LINE") {
    P a{src.f(10), src.f(20)}, b{src.f(11), src.f(21)};
    P n = leftNormal(a, b);
    if (n.x == 0 && n.y == 0) return false;
    double s = cross(a, b, p) >= 0 ? d : -d;
    for (auto& t : out.tags) {
      if (t.code == 10) t.value = num(a.x + n.x * s);
      else if (t.code == 20) t.value = num(a.y + n.y * s);
      else if (t.code == 11) t.value = num(b.x + n.x * s);
      else if (t.code == 21) t.value = num(b.y + n.y * s);
    }
    return true;
  }

  if (src.type == "CIRCLE" || src.type == "ARC") {
    double cx = src.f(10), cy = src.f(20), r = src.f(40);
    double nr = std::hypot(p.x - cx, p.y - cy) > r ? r + d : r - d;
    if (nr <= 1e-9) return false;
    for (auto& t : out.tags)
      if (t.code == 40) t.value = num(nr);
    return true;
  }

  if (src.type == "LWPOLYLINE") {
    std::vector<P> v;
    for (size_t k = 0; k < src.tags.size(); k++) {
      const Tag& t = src.tags[k];
      if (t.code == 42 && std::fabs(t.d()) > 1e-9) return false;  // yaylı parçalar: henüz desteklenmiyor
      if (t.code == 10) {
        double y = (k + 1 < src.tags.size() && src.tags[k + 1].code == 20) ? src.tags[k + 1].d() : 0;
        v.push_back({t.d(), y});
      }
    }
    // ardışık çakışık noktaları ayıkla (normal hesaplanamaz)
    std::vector<P> u;
    for (const P& q : v)
      if (u.empty() || std::hypot(q.x - u.back().x, q.y - u.back().y) > 1e-9) u.push_back(q);
    bool closed = (src.n(70) & 1) != 0;
    if (closed && u.size() > 2 && std::hypot(u[0].x - u.back().x, u[0].y - u.back().y) < 1e-9) u.pop_back();
    if (u.size() < 2 || u.size() != v.size()) return false;
    size_t n = u.size(), segs = closed ? n : n - 1;

    // taraf: noktaya en yakın parçaya göre
    size_t best = 0;
    double bd = 1e300;
    for (size_t i = 0; i < segs; i++) {
      double dd = segDist(p, u[i], u[(i + 1) % n]);
      if (dd < bd) { bd = dd; best = i; }
    }
    double s = cross(u[best], u[(best + 1) % n], p) >= 0 ? d : -d;

    std::vector<P> base(segs), dir(segs);
    for (size_t i = 0; i < segs; i++) {
      P a = u[i], b = u[(i + 1) % n], nn = leftNormal(a, b);
      base[i] = {a.x + nn.x * s, a.y + nn.y * s};
      dir[i] = {b.x - a.x, b.y - a.y};
    }
    std::vector<P> w(n);
    for (size_t i = 0; i < n; i++) {
      bool hasPrev = closed || i > 0, hasNext = closed || i < n - 1;
      size_t sp = (i + segs - 1) % segs, sn = i % segs;
      if (hasPrev && hasNext) {
        if (!intersect(base[sp], dir[sp], base[sn], dir[sn], w[i])) {
          P nn = leftNormal(u[i], u[(i + 1) % n]);
          w[i] = {u[i].x + nn.x * s, u[i].y + nn.y * s};
        }
      } else if (hasNext) {
        w[i] = base[sn];
      } else {
        // açık çoklu çizginin son noktası: son parçanın ötelenmiş ucu
        w[i] = {base[sp].x + dir[sp].x, base[sp].y + dir[sp].y};
      }
    }
    size_t vi = 0;
    for (size_t k = 0; k < out.tags.size(); k++) {
      if (out.tags[k].code != 10 || vi >= w.size()) continue;
      out.tags[k].value = num(w[vi].x);
      if (k + 1 < out.tags.size() && out.tags[k + 1].code == 20) out.tags[k + 1].value = num(w[vi].y);
      vi++;
    }
    return true;
  }
  return false;
}

}  // namespace mk
