// Düzenleme çekirdeği: seçme, yakalama, taşıma ve geri al / yinele.
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <limits>
#include <set>

#include "document.h"

namespace mk {

namespace {

constexpr double kPi = 3.14159265358979323846;

double segDist(double px, double py, double ax, double ay, double bx, double by) {
  double vx = bx - ax, vy = by - ay;
  double l2 = vx * vx + vy * vy;
  double t = l2 > 0 ? ((px - ax) * vx + (py - ay) * vy) / l2 : 0;
  t = std::clamp(t, 0.0, 1.0);
  return std::hypot(px - (ax + t * vx), py - (ay + t * vy));
}

bool inPoly(double px, double py, const float* v, int n) {
  bool in = false;
  for (int i = 0, j = n - 1; i < n; j = i++) {
    double xi = v[i * 2], yi = v[i * 2 + 1], xj = v[j * 2], yj = v[j * 2 + 1];
    if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi)) in = !in;
  }
  return in;
}

// Kendi düzleminde (OCS) tanımlı nesne türleri: Z = -1 ise X aynalanır.
bool isOcsType(const std::string& t) {
  static const std::set<std::string> s{"CIRCLE", "ARC", "LWPOLYLINE", "POLYLINE", "VERTEX", "TEXT", "ATTRIB",
                                       "INSERT", "SOLID", "TRACE", "HATCH"};
  return s.count(t) > 0;
}

std::string num(double v) {
  char buf[40];
  std::snprintf(buf, sizeof(buf), "%.12g", v);
  return buf;
}

}  // namespace

// ───────────────────────── seçme ─────────────────────────

int pickEntity(Document& doc, double x, double y, double tol) {
  int best = -1;
  double bestD = tol;
  const float* v = doc.vertices.data();
  for (size_t i = 0; i < doc.segOwner.size(); i++) {
    const float* s = v + i * 4;
    // hızlı kutu reddi
    if (x < std::min(s[0], s[2]) - tol || x > std::max(s[0], s[2]) + tol || y < std::min(s[1], s[3]) - tol ||
        y > std::max(s[1], s[3]) + tol)
      continue;
    double d = segDist(x, y, s[0], s[1], s[2], s[3]);
    if (d < bestD) {
      bestD = d;
      best = doc.segOwner[i];
    }
  }
  // yazılar: yaklaşık kutunun içi
  for (size_t i = 0; i < doc.texts.size(); i++) {
    const MkText& t = doc.texts[i];
    double r = t.rotation * kPi / 180.0;
    double lx = (x - t.x) * std::cos(r) + (y - t.y) * std::sin(r);
    double ly = -(x - t.x) * std::sin(r) + (y - t.y) * std::cos(r);
    int len = 0;
    for (int k = 0; k < t.utf8_len; k++)
      if ((doc.textPool[t.utf8_offset + k] & 0xC0) != 0x80) len++;
    double w = t.height * 0.62 * len * t.width_factor, h = t.height;
    double ox = t.halign == 1 ? -w / 2 : (t.halign == 2 ? -w : 0);
    double oy = t.valign == 3 ? -h : (t.valign == 2 ? -h / 2 : 0);
    if (lx >= ox - tol * 0.3 && lx <= ox + w + tol * 0.3 && ly >= oy - tol * 0.3 && ly <= oy + h + tol * 0.3) {
      double d = tol * 0.4;
      if (d < bestD) {
        bestD = d;
        best = doc.textOwner[i];
      }
    }
  }
  // dolgular: çizgi bulunamadıysa içi
  if (best < 0) {
    for (size_t i = 0; i < doc.polys.size(); i++) {
      const MkPoly& p = doc.polys[i];
      if (inPoly(x, y, v + p.offset, p.npoints)) {
        best = doc.polyOwner[i];
        break;
      }
    }
  }
  return best;
}

// ───────────────────────── yakalama ─────────────────────────

int snapPoint(Document& doc, double x, double y, double tol, double& ox, double& oy) {
  int bestType = 0;
  double bestD = tol;
  auto cand = [&](double px, double py, int type) {
    double d = std::hypot(px - x, py - y);
    // uç nokta, orta noktaya göre biraz öncelikli
    if (type == 1) d *= 0.9;
    if (d < bestD) {
      bestD = d;
      bestType = type;
      ox = px;
      oy = py;
    }
  };
  for (const auto& e : doc.entities) {
    if (e.deleted) continue;
    double sx = (isOcsType(e.type) && e.flipped()) ? -1 : 1;
    const std::string& t = e.type;
    if (t == "LINE") {
      double x1 = e.f(10), y1 = e.f(20), x2 = e.f(11), y2 = e.f(21);
      cand(x1, y1, 1);
      cand(x2, y2, 1);
      cand((x1 + x2) / 2, (y1 + y2) / 2, 2);
    } else if (t == "LWPOLYLINE") {
      std::vector<std::pair<double, double>> p;
      for (size_t k = 0; k < e.tags.size(); k++)
        if (e.tags[k].code == 10 && k + 1 < e.tags.size()) p.emplace_back(sx * e.tags[k].d(), e.tags[k + 1].d());
      for (size_t k = 0; k < p.size(); k++) {
        cand(p[k].first, p[k].second, 1);
        if (k + 1 < p.size()) cand((p[k].first + p[k + 1].first) / 2, (p[k].second + p[k + 1].second) / 2, 2);
      }
    } else if (t == "CIRCLE") {
      cand(sx * e.f(10), e.f(20), 3);
    } else if (t == "ARC") {
      double cx = e.f(10), cy = e.f(20), r = e.f(40);
      double a0 = e.f(50) * kPi / 180, a1 = e.f(51) * kPi / 180;
      cand(sx * cx, cy, 3);
      cand(sx * (cx + r * std::cos(a0)), cy + r * std::sin(a0), 1);
      cand(sx * (cx + r * std::cos(a1)), cy + r * std::sin(a1), 1);
    } else if (t == "ELLIPSE") {
      cand(e.f(10), e.f(20), 3);
    } else if (t == "INSERT" || t == "TEXT" || t == "MTEXT" || t == "POINT") {
      cand((t == "MTEXT" || t == "POINT" ? 1 : sx) * e.f(10), e.f(20), 4);
    }
  }
  return bestType;
}

// ───────────────────────── taşıma ─────────────────────────

static bool translateTags(const std::string& type, std::vector<Tag>& tags, double dx, double dy, bool flipped) {
  double sx = (isOcsType(type) && flipped) ? -1 : 1;
  std::set<int> vectors;
  if (type == "MTEXT" || type == "ELLIPSE" || type == "XLINE" || type == "RAY") vectors.insert(11);
  if (type == "SPLINE") vectors = {12, 13};
  int lastEdge = 0;
  for (size_t k = 0; k < tags.size(); k++) {
    int c = tags[k].code;
    if (type == "HATCH" && c == 72) lastEdge = tags[k].i();
    if (c < 10 || c > 18 || vectors.count(c)) continue;
    if (type == "HATCH" && c == 11 && lastEdge == 3) continue;  // elips kenarının ana ekseni (vektör)
    for (size_t j = k + 1; j < tags.size() && j <= k + 3; j++) {
      if (tags[j].code == c + 10) {
        tags[k].value = num(tags[k].d() + sx * dx);
        tags[j].value = num(tags[j].d() + dy);
        break;
      }
    }
  }
  return true;
}

bool translateEntity(Entity& e, double dx, double dy) {
  static const std::set<std::string> unsupported{"DIMENSION", "VIEWPORT", "OLE2FRAME", "IMAGE", "MLINE"};
  if (unsupported.count(e.type)) return false;
  translateTags(e.type, e.tags, dx, dy, e.flipped());
  for (auto& c : e.children) translateTags(c.type, c.tags, dx, dy, c.type == "VERTEX" ? e.flipped() : c.flipped());
  return true;
}

// ───────────────────────── pencere seçimi ─────────────────────────

namespace {

bool segHitsRect(double ax, double ay, double bx, double by, double x0, double y0, double x1, double y1) {
  auto in = [&](double x, double y) { return x >= x0 && x <= x1 && y >= y0 && y <= y1; };
  if (in(ax, ay) || in(bx, by)) return true;
  // Liang–Barsky kırpma
  double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
  const double p[4] = {-dx, dx, -dy, dy};
  const double q[4] = {ax - x0, x1 - ax, ay - y0, y1 - ay};
  for (int i = 0; i < 4; i++) {
    if (p[i] == 0) {
      if (q[i] < 0) return false;
    } else {
      double t = q[i] / p[i];
      if (p[i] < 0) t0 = std::max(t0, t);
      else t1 = std::min(t1, t);
      if (t0 > t1) return false;
    }
  }
  return true;
}

}  // namespace

int selectRect(Document& doc, double x0, double y0, double x1, double y1, bool crossing, std::vector<int32_t>& out) {
  if (x0 > x1) std::swap(x0, x1);
  if (y0 > y1) std::swap(y0, y1);
  // bit 1: dikdörtgene değen parça var · bit 2: tamamen içeride olmayan parça var
  std::vector<uint8_t> f(doc.entities.size(), 0);
  auto inside = [&](double x, double y) { return x >= x0 && x <= x1 && y >= y0 && y <= y1; };
  auto mark = [&](int owner, bool hit, bool fully) {
    if (owner < 0 || owner >= static_cast<int>(f.size())) return;
    if (hit) f[owner] |= 1;
    if (!fully) f[owner] |= 2;
  };
  const float* v = doc.vertices.data();
  for (const auto& b : doc.batches) {
    for (int64_t k = b.offset; k + 3 < b.offset + b.count; k += 4) {
      int owner = doc.segOwner[static_cast<size_t>(k / 4)];
      double ax = v[k], ay = v[k + 1], bx = v[k + 2], by = v[k + 3];
      bool fully = inside(ax, ay) && inside(bx, by);
      mark(owner, fully || segHitsRect(ax, ay, bx, by, x0, y0, x1, y1), fully);
    }
  }
  for (size_t i = 0; i < doc.polys.size(); i++) {
    const auto& p = doc.polys[i];
    bool any = false, all = true;
    for (int j = 0; j < p.npoints; j++) {
      bool in = inside(v[p.offset + j * 2], v[p.offset + j * 2 + 1]);
      any |= in;
      all &= in;
    }
    mark(doc.polyOwner[i], any, all);
  }
  for (size_t i = 0; i < doc.texts.size(); i++) {
    bool in = inside(doc.texts[i].x, doc.texts[i].y);
    mark(doc.textOwner[i], in, in);
  }
  out.clear();
  for (size_t i = 0; i < f.size(); i++) {
    if (doc.entities[i].deleted) continue;
    bool sel = crossing ? (f[i] & 1) != 0 : (f[i] == 1);
    if (sel) out.push_back(static_cast<int32_t>(i));
  }
  return static_cast<int>(out.size());
}

// ───────────────────────── geri al / yinele ─────────────────────────

void beginOp(Document& doc) {
  doc.pending.changes.clear();
  doc.inOp = true;
}

void touch(Document& doc, int idx) {
  for (const auto& c : doc.pending.changes)
    if (c.idx == idx) return;
  Change ch;
  ch.idx = idx;
  ch.before = doc.entities[idx];
  doc.pending.changes.push_back(std::move(ch));
}

void commitOp(Document& doc) {
  if (doc.pending.changes.empty()) {
    doc.inOp = false;
    return;
  }
  for (auto& c : doc.pending.changes) c.after = doc.entities[c.idx];
  doc.undo.push_back(std::move(doc.pending));
  doc.pending = Operation();
  doc.redo.clear();
  if (doc.undo.size() > 200) doc.undo.erase(doc.undo.begin());
  doc.inOp = false;
  doc.dirty = true;
}

bool undoOp(Document& doc) {
  if (doc.undo.empty()) return false;
  Operation op = std::move(doc.undo.back());
  doc.undo.pop_back();
  for (auto it = op.changes.rbegin(); it != op.changes.rend(); ++it) doc.entities[it->idx] = it->before;
  doc.redo.push_back(std::move(op));
  doc.dirty = true;
  return true;
}

bool redoOp(Document& doc) {
  if (doc.redo.empty()) return false;
  Operation op = std::move(doc.redo.back());
  doc.redo.pop_back();
  for (const auto& c : op.changes) doc.entities[c.idx] = c.after;
  doc.undo.push_back(std::move(op));
  doc.dirty = true;
  return true;
}

}  // namespace mk
