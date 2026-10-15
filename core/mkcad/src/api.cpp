// mkcad C arayüzü.
#include <algorithm>
#include <cstdio>
#include <exception>
#include <memory>
#include <string>

#include "document.h"
#include "mkcad.h"
#include "tables.h"

struct MkDoc {
  std::unique_ptr<mk::Document> doc;
};

namespace {
thread_local std::string g_error;
const char* cstr(const std::string& s) { return s.c_str(); }
}  // namespace

extern "C" {

MK_API const char* mk_version(void) { return "mkcad 0.1.0"; }

MK_API MkDoc* mk_open(const uint8_t* data, int64_t len) {
  g_error.clear();
  if (!data || len <= 0) {
    g_error = "Bos veri.";
    return nullptr;
  }
  try {
    auto d = mk::readDxf(data, static_cast<size_t>(len), g_error);
    if (!d) return nullptr;
    auto* h = new MkDoc();
    h->doc = std::move(d);
    return h;
  } catch (const std::exception& e) {
    g_error = std::string("Okuma hatasi: ") + e.what();
  } catch (...) {
    g_error = "Okuma sirasinda bilinmeyen hata.";
  }
  return nullptr;
}

MK_API const char* mk_last_error(void) { return g_error.c_str(); }
MK_API void mk_close(MkDoc* d) { delete d; }

MK_API const char* mk_dxf_version(MkDoc* d) { return cstr(d->doc->version); }
MK_API const char* mk_codepage(MkDoc* d) { return cstr(d->doc->codepage); }
MK_API int32_t mk_entity_count(MkDoc* d) { return static_cast<int32_t>(d->doc->entities.size()); }
MK_API int32_t mk_block_count(MkDoc* d) { return static_cast<int32_t>(d->doc->blocks.size()); }
MK_API const char* mk_type_stats(MkDoc* d) { return cstr(d->doc->typeStats); }

MK_API int32_t mk_layer_count(MkDoc* d) { return static_cast<int32_t>(d->doc->layers.size()); }
MK_API const char* mk_layer_name(MkDoc* d, int32_t i) { return cstr(d->doc->layers.at(i).name); }
MK_API uint32_t mk_layer_rgb(MkDoc* d, int32_t i) {
  return mk::kAciRgb[d->doc->layers.at(i).aci & 255];
}
MK_API int32_t mk_layer_flags(MkDoc* d, int32_t i) {
  const auto& l = d->doc->layers.at(i);
  return (l.off ? 1 : 0) | (l.frozen ? 2 : 0);
}
MK_API int32_t mk_layer_entity_count(MkDoc* d, int32_t i) { return d->doc->layers.at(i).count; }

MK_API int32_t mk_build_scene(MkDoc* d) {
  try {
    mk::buildScene(*d->doc);
    return 0;
  } catch (const std::exception& e) {
    g_error = std::string("Sahne hatasi: ") + e.what();
    return -1;
  } catch (...) {
    g_error = "Sahne kurulurken bilinmeyen hata.";
    return -1;
  }
}

MK_API void mk_extents(MkDoc* d, float* out4) {
  for (int i = 0; i < 4; i++) out4[i] = d->doc->ext[i];
}
MK_API const float* mk_vertices(MkDoc* d) { return d->doc->vertices.data(); }
MK_API int64_t mk_vertex_float_count(MkDoc* d) { return static_cast<int64_t>(d->doc->vertices.size()); }
MK_API int32_t mk_batch_count(MkDoc* d) { return static_cast<int32_t>(d->doc->batches.size()); }
MK_API const MkBatch* mk_batches(MkDoc* d) { return d->doc->batches.data(); }
MK_API int32_t mk_poly_count(MkDoc* d) { return static_cast<int32_t>(d->doc->polys.size()); }
MK_API const MkPoly* mk_polys(MkDoc* d) { return d->doc->polys.data(); }
MK_API int32_t mk_text_count(MkDoc* d) { return static_cast<int32_t>(d->doc->texts.size()); }
MK_API const MkText* mk_texts(MkDoc* d) { return d->doc->texts.data(); }
MK_API const char* mk_text_pool(MkDoc* d) { return d->doc->textPool.c_str(); }

// ───────────────────────── düzenleme ─────────────────────────

namespace {

std::string fmtNum(double v) {
  char buf[40];
  std::snprintf(buf, sizeof(buf), "%.12g", v);
  return buf;
}

bool validId(MkDoc* d, int32_t id) { return id >= 0 && id < static_cast<int32_t>(d->doc->entities.size()); }

/// Yeni nesnenin ortak başlık etiketleri (R13+ için handle, sahip ve alt sınıf).
std::vector<mk::Tag> header(mk::Document& doc, const char* layer, const char* subclass) {
  std::vector<mk::Tag> t;
  bool r13 = doc.versionNumber() >= 1012;
  if (r13 || doc.maxHandle > 0) t.push_back({5, doc.newHandle()});
  if (r13 && !doc.modelOwner.empty()) t.push_back({330, doc.modelOwner});
  if (r13) t.push_back({100, "AcDbEntity"});
  t.push_back({8, layer && *layer ? layer : "0"});
  if (r13) t.push_back({100, subclass});
  return t;
}

int32_t append(mk::Document& doc, mk::Entity e) {
  mk::beginOp(doc);
  doc.entities.push_back(std::move(e));
  int idx = static_cast<int>(doc.entities.size()) - 1;
  mk::Change ch;
  ch.idx = idx;
  ch.before = doc.entities[idx];
  ch.before.deleted = true;  // geri alınınca yok olsun
  doc.pending.changes.push_back(std::move(ch));
  mk::commitOp(doc);
  return idx;
}

void renewHandles(mk::Document& doc, std::vector<mk::Tag>& tags) {
  for (auto& t : tags)
    if (t.code == 5) t.value = doc.newHandle();
}

}  // namespace

MK_API int32_t mk_pick(MkDoc* d, double x, double y, double tol) { return mk::pickEntity(*d->doc, x, y, tol); }

MK_API int32_t mk_select_rect(MkDoc* d, double x0, double y0, double x1, double y1, int32_t crossing, int32_t* out,
                              int32_t cap) {
  std::vector<int32_t> ids;
  int n = mk::selectRect(*d->doc, x0, y0, x1, y1, crossing != 0, ids);
  for (int i = 0; i < n && i < cap; i++) out[i] = ids[i];
  return n;
}

MK_API int32_t mk_snap(MkDoc* d, double x, double y, double tol, double* out2) {
  double ox = x, oy = y;
  int t = mk::snapPoint(*d->doc, x, y, tol, ox, oy);
  out2[0] = ox;
  out2[1] = oy;
  return t;
}

MK_API const float* mk_entity_geometry(MkDoc* d, int32_t id, int64_t* count) {
  mk::entityGeometry(*d->doc, id, d->doc->scratch);
  *count = static_cast<int64_t>(d->doc->scratch.size());
  return d->doc->scratch.data();
}

MK_API const char* mk_entity_info(MkDoc* d, int32_t id) {
  auto& s = d->doc->scratchStr;
  s.clear();
  if (!validId(d, id)) return s.c_str();
  const auto& e = d->doc->entities[id];
  s = e.type + "|" + e.layer() + "|" + e.s(5);
  if (e.type == "INSERT") s += "|" + e.s(2);
  return s.c_str();
}

MK_API int32_t mk_move(MkDoc* d, const int32_t* ids, int32_t n, double dx, double dy) {
  auto& doc = *d->doc;
  mk::beginOp(doc);
  int moved = 0;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::touch(doc, ids[i]);
    if (mk::translateEntity(doc.entities[ids[i]], dx, dy)) moved++;
  }
  mk::commitOp(doc);
  return moved;
}

MK_API int32_t mk_copy(MkDoc* d, const int32_t* ids, int32_t n, double dx, double dy) {
  auto& doc = *d->doc;
  mk::beginOp(doc);
  int first = -1;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::Entity c = doc.entities[ids[i]];
    if (!mk::translateEntity(c, dx, dy)) continue;
    renewHandles(doc, c.tags);
    for (auto& ch : c.children) renewHandles(doc, ch.tags);
    renewHandles(doc, c.seqend);
    doc.entities.push_back(std::move(c));
    int idx = static_cast<int>(doc.entities.size()) - 1;
    if (first < 0) first = idx;
    mk::Change ch;
    ch.idx = idx;
    ch.before = doc.entities[idx];
    ch.before.deleted = true;
    doc.pending.changes.push_back(std::move(ch));
  }
  mk::commitOp(doc);
  return first;
}

MK_API int32_t mk_delete(MkDoc* d, const int32_t* ids, int32_t n) {
  auto& doc = *d->doc;
  mk::beginOp(doc);
  int del = 0;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::touch(doc, ids[i]);
    doc.entities[ids[i]].deleted = true;
    del++;
  }
  mk::commitOp(doc);
  return del;
}

MK_API int32_t mk_add_line(MkDoc* d, const char* layer, double x1, double y1, double x2, double y2) {
  auto& doc = *d->doc;
  mk::Entity e;
  e.type = "LINE";
  e.tags = header(doc, layer, "AcDbLine");
  e.tags.insert(e.tags.end(), {{10, fmtNum(x1)}, {20, fmtNum(y1)}, {30, "0.0"}, {11, fmtNum(x2)}, {21, fmtNum(y2)}, {31, "0.0"}});
  return append(doc, std::move(e));
}

MK_API int32_t mk_add_text(MkDoc* d, const char* layer, double x, double y, double h, double rot, const char* utf8) {
  auto& doc = *d->doc;
  mk::Entity e;
  e.type = "TEXT";
  e.tags = header(doc, layer, "AcDbText");
  e.tags.insert(e.tags.end(), {{10, fmtNum(x)}, {20, fmtNum(y)}, {30, "0.0"}, {40, fmtNum(h)}, {1, utf8 ? utf8 : ""}});
  if (rot != 0) e.tags.push_back({50, fmtNum(rot)});
  if (doc.versionNumber() >= 1012) e.tags.push_back({100, "AcDbText"});
  return append(doc, std::move(e));
}

namespace {

int32_t transformIds(MkDoc* d, const int32_t* ids, int32_t n, const mk::Xf& X) {
  auto& doc = *d->doc;
  mk::beginOp(doc);
  int done = 0;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::Entity probe = doc.entities[ids[i]];
    if (!mk::transformEntity(probe, X)) continue;
    mk::touch(doc, ids[i]);
    doc.entities[ids[i]] = std::move(probe);
    done++;
  }
  mk::commitOp(doc);
  return done;
}

}  // namespace

MK_API int32_t mk_rotate(MkDoc* d, const int32_t* ids, int32_t n, double cx, double cy, double deg) {
  return transformIds(d, ids, n, mk::makeRotate(cx, cy, deg));
}

MK_API int32_t mk_scale(MkDoc* d, const int32_t* ids, int32_t n, double cx, double cy, double k) {
  if (!(k > 0)) return 0;
  return transformIds(d, ids, n, mk::makeScale(cx, cy, k));
}

MK_API int32_t mk_mirror(MkDoc* d, const int32_t* ids, int32_t n, double x1, double y1, double x2, double y2,
                         int32_t keep) {
  if (x1 == x2 && y1 == y2) return keep ? -1 : 0;
  mk::Xf X = mk::makeMirror(x1, y1, x2, y2);
  if (!keep) return transformIds(d, ids, n, X);
  auto& doc = *d->doc;
  mk::beginOp(doc);
  int first = -1;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::Entity c = doc.entities[ids[i]];
    if (!mk::transformEntity(c, X)) continue;
    renewHandles(doc, c.tags);
    for (auto& ch : c.children) renewHandles(doc, ch.tags);
    renewHandles(doc, c.seqend);
    doc.entities.push_back(std::move(c));
    int idx = static_cast<int>(doc.entities.size()) - 1;
    if (first < 0) first = idx;
    mk::Change ch;
    ch.idx = idx;
    ch.before = doc.entities[idx];
    ch.before.deleted = true;
    doc.pending.changes.push_back(std::move(ch));
  }
  mk::commitOp(doc);
  return first;
}

MK_API const char* mk_entity_text(MkDoc* d, int32_t id) {
  auto& s = d->doc->scratchStr;
  s = validId(d, id) ? mk::entityText(d->doc->entities[id]) : "";
  return s.c_str();
}

MK_API int32_t mk_set_text(MkDoc* d, int32_t id, const char* utf8) {
  if (!validId(d, id) || !utf8) return 0;
  auto& doc = *d->doc;
  mk::Entity e = doc.entities[id];
  if (!mk::setEntityText(e, utf8)) return 0;
  mk::beginOp(doc);
  mk::touch(doc, id);
  doc.entities[id] = std::move(e);
  mk::commitOp(doc);
  return 1;
}

MK_API int32_t mk_set_layer(MkDoc* d, const int32_t* ids, int32_t n, const char* layer) {
  if (!layer || !*layer) return 0;
  auto& doc = *d->doc;
  doc.layerOf(layer);
  mk::beginOp(doc);
  int done = 0;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::touch(doc, ids[i]);
    mk::setEntityLayer(doc.entities[ids[i]], layer);
    done++;
  }
  mk::commitOp(doc);
  return done;
}

MK_API int32_t mk_set_color(MkDoc* d, const int32_t* ids, int32_t n, int32_t aci) {
  if (aci < 0 || aci > 256) return 0;
  auto& doc = *d->doc;
  mk::beginOp(doc);
  int done = 0;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    mk::touch(doc, ids[i]);
    auto& e = doc.entities[ids[i]];
    if (e.type == "INSERT") {
      // blok içi nesneler kendi rengindeyse yerleştirme rengi işlemez: yeniden renklendirilmiş blok kopyası
      std::string err;
      if (!mk::recolorInsert(doc, e, aci, err)) continue;
    } else {
      mk::setEntityColor(e, aci);
    }
    done++;
  }
  mk::commitOp(doc);
  return done;
}

MK_API int32_t mk_add_circle(MkDoc* d, const char* layer, double cx, double cy, double r) {
  if (!(r > 0)) return -1;
  auto& doc = *d->doc;
  mk::Entity e;
  e.type = "CIRCLE";
  e.tags = header(doc, layer, "AcDbCircle");
  e.tags.insert(e.tags.end(), {{10, fmtNum(cx)}, {20, fmtNum(cy)}, {30, "0.0"}, {40, fmtNum(r)}});
  return append(doc, std::move(e));
}

MK_API int32_t mk_add_polyline(MkDoc* d, const char* layer, const double* xy, int32_t n, int32_t closed) {
  if (!xy || n < 2) return -1;
  auto& doc = *d->doc;
  if (doc.versionNumber() < 1012) {
    // R12: LWPOLYLINE yok → ayrı çizgiler, tek geri alma adımında
    mk::beginOp(doc);
    int first = -1;
    int segs = closed && n > 2 ? n : n - 1;
    for (int i = 0; i < segs; i++) {
      int j = (i + 1) % n;
      mk::Entity e;
      e.type = "LINE";
      e.tags = header(doc, layer, "AcDbLine");
      e.tags.insert(e.tags.end(), {{10, fmtNum(xy[i * 2])}, {20, fmtNum(xy[i * 2 + 1])}, {30, "0.0"},
                                   {11, fmtNum(xy[j * 2])}, {21, fmtNum(xy[j * 2 + 1])}, {31, "0.0"}});
      doc.entities.push_back(std::move(e));
      int idx = static_cast<int>(doc.entities.size()) - 1;
      if (first < 0) first = idx;
      mk::Change ch;
      ch.idx = idx;
      ch.before = doc.entities[idx];
      ch.before.deleted = true;
      doc.pending.changes.push_back(std::move(ch));
    }
    mk::commitOp(doc);
    return first;
  }
  mk::Entity e;
  e.type = "LWPOLYLINE";
  e.tags = header(doc, layer, "AcDbPolyline");
  e.tags.push_back({90, std::to_string(n)});
  e.tags.push_back({70, closed ? "1" : "0"});
  e.tags.push_back({43, "0.0"});
  for (int i = 0; i < n; i++) {
    e.tags.push_back({10, fmtNum(xy[i * 2])});
    e.tags.push_back({20, fmtNum(xy[i * 2 + 1])});
  }
  return append(doc, std::move(e));
}

MK_API int32_t mk_offset(MkDoc* d, int32_t id, double dist, double px, double py) {
  if (!validId(d, id)) return -1;
  auto& doc = *d->doc;
  if (doc.entities[id].deleted) return -1;
  mk::Entity e;
  if (!mk::offsetEntity(doc.entities[id], dist, px, py, e)) return -1;
  renewHandles(doc, e.tags);
  return append(doc, std::move(e));
}

MK_API int32_t mk_define_block(MkDoc* d, const char* name, const char* snippet) {
  g_error.clear();
  if (!name || !snippet) return 0;
  try {
    return mk::defineBlock(*d->doc, name, snippet, g_error) ? 1 : 0;
  } catch (const std::exception& e) {
    g_error = e.what();
    return 0;
  }
}

MK_API int32_t mk_insert_block(MkDoc* d, const char* layer, const char* name, double x, double y, double scale,
                               double rotation) {
  auto& doc = *d->doc;
  if (!name || !doc.blocks.count(name) || !(scale > 0)) return -1;
  mk::Entity e;
  e.type = "INSERT";
  e.tags = header(doc, layer, "AcDbBlockReference");
  e.tags.insert(e.tags.end(), {{2, name}, {10, fmtNum(x)}, {20, fmtNum(y)}, {30, "0.0"}, {41, fmtNum(scale)},
                               {42, fmtNum(scale)}, {43, fmtNum(scale)}});
  if (rotation != 0) e.tags.push_back({50, fmtNum(rotation)});
  return append(doc, std::move(e));
}

MK_API const char* mk_insert_counts(MkDoc* d) {
  auto& s = d->doc->scratchStr;
  s = mk::insertCounts(*d->doc);
  return s.c_str();
}

MK_API int32_t mk_group(MkDoc* d, const int32_t* ids, int32_t n) {
  auto& doc = *d->doc;
  g_error.clear();
  std::vector<int> v;
  double x0 = 1e300, y0 = 1e300, x1 = -1e300, y1 = -1e300;
  std::vector<float> g;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted) continue;
    v.push_back(ids[i]);
    mk::entityGeometry(doc, ids[i], g);
    for (size_t k = 0; k + 1 < g.size(); k += 2) {
      x0 = std::min(x0, (double)g[k]); x1 = std::max(x1, (double)g[k]);
      y0 = std::min(y0, (double)g[k + 1]); y1 = std::max(y1, (double)g[k + 1]);
    }
  }
  if (v.size() < 2) {
    g_error = "Gruplamak için en az iki nesne seçin.";
    return -1;
  }
  double bx = x0 <= x1 ? (x0 + x1) / 2 : 0, by = y0 <= y1 ? (y0 + y1) / 2 : 0;
  std::string name = mk::groupName(doc);
  if (!mk::makeGroupBlock(doc, v, name, bx, by, g_error)) return -1;
  mk::beginOp(doc);
  for (int id : v) {
    mk::touch(doc, id);
    doc.entities[id].deleted = true;
  }
  mk::Entity e;
  e.type = "INSERT";
  e.tags = header(doc, "0", "AcDbBlockReference");
  e.tags.insert(e.tags.end(), {{2, name}, {10, fmtNum(bx)}, {20, fmtNum(by)}, {30, "0.0"}});
  doc.entities.push_back(std::move(e));
  int idx = static_cast<int>(doc.entities.size()) - 1;
  mk::Change ch;
  ch.idx = idx;
  ch.before = doc.entities[idx];
  ch.before.deleted = true;
  doc.pending.changes.push_back(std::move(ch));
  mk::commitOp(doc);
  return idx;
}

MK_API int32_t mk_explode(MkDoc* d, const int32_t* ids, int32_t n, int32_t* count) {
  auto& doc = *d->doc;
  g_error.clear();
  *count = 0;
  mk::beginOp(doc);
  int first = -1;
  for (int i = 0; i < n; i++) {
    if (!validId(d, ids[i]) || doc.entities[ids[i]].deleted || doc.entities[ids[i]].type != "INSERT") continue;
    std::vector<mk::Entity> parts;
    if (!mk::explodeInsert(doc, doc.entities[ids[i]], parts, g_error)) continue;
    mk::touch(doc, ids[i]);
    doc.entities[ids[i]].deleted = true;
    for (auto& p : parts) {
      renewHandles(doc, p.tags);
      bool hasHandle = false;
      for (auto& t : p.tags) hasHandle |= t.code == 5;
      if (!hasHandle && (doc.versionNumber() >= 1012 || doc.maxHandle > 0)) p.tags.insert(p.tags.begin(), {5, doc.newHandle()});
      if (doc.versionNumber() >= 1012 && !doc.modelOwner.empty()) {
        bool owner = false;
        for (auto& t : p.tags)
          if (t.code == 330) { t.value = doc.modelOwner; owner = true; }
        if (!owner) p.tags.insert(p.tags.begin() + 1, {330, doc.modelOwner});
      }
      for (auto& c : p.children) renewHandles(doc, c.tags);
      renewHandles(doc, p.seqend);
      doc.entities.push_back(std::move(p));
      int idx = static_cast<int>(doc.entities.size()) - 1;
      if (first < 0) first = idx;
      (*count)++;
      mk::Change ch;
      ch.idx = idx;
      ch.before = doc.entities[idx];
      ch.before.deleted = true;
      doc.pending.changes.push_back(std::move(ch));
    }
  }
  mk::commitOp(doc);
  return first;
}

MK_API int32_t mk_undo(MkDoc* d) { return mk::undoOp(*d->doc) ? 1 : 0; }
MK_API int32_t mk_redo(MkDoc* d) { return mk::redoOp(*d->doc) ? 1 : 0; }
MK_API int32_t mk_undo_count(MkDoc* d) { return static_cast<int32_t>(d->doc->undo.size()); }
MK_API int32_t mk_redo_count(MkDoc* d) { return static_cast<int32_t>(d->doc->redo.size()); }
MK_API int32_t mk_is_dirty(MkDoc* d) { return d->doc->dirty ? 1 : 0; }

MK_API const uint8_t* mk_save(MkDoc* d, int64_t* len) {
  auto& s = d->doc->scratchStr;
  s = mk::writeDxf(*d->doc);
  d->doc->dirty = false;
  *len = static_cast<int64_t>(s.size());
  return reinterpret_cast<const uint8_t*>(s.data());
}

MK_API const uint8_t* mk_save_copy(MkDoc* d, int64_t* len) {
  auto& s = d->doc->scratchStr;
  s = mk::writeDxf(*d->doc);  // "kaydedilmedi" işareti korunur (otomatik kayıt)
  *len = static_cast<int64_t>(s.size());
  return reinterpret_cast<const uint8_t*>(s.data());
}

MK_API void mk_set_dirty(MkDoc* d, int32_t dirty) { d->doc->dirty = dirty != 0; }

}  // extern "C"
