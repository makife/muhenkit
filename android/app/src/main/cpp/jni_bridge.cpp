// JNI köprüsü: com.muhenkit.app.cad.MkNative ↔ mkcad C arayüzü.
// Metinler JNI'nin "modified UTF-8" tuzağına düşmemek için ham UTF-8 bayt olarak verilir.
#include <jni.h>

#include <algorithm>
#include <cstring>
#include <string>
#include <vector>

#include "mkcad.h"
#include "mkcalc.h"

namespace {

inline MkDoc* D(jlong h) { return reinterpret_cast<MkDoc*>(h); }

jbyteArray bytes(JNIEnv* env, const char* p, size_t n) {
  jbyteArray a = env->NewByteArray(static_cast<jsize>(n));
  if (a && n) env->SetByteArrayRegion(a, 0, static_cast<jsize>(n), reinterpret_cast<const jbyte*>(p));
  return a;
}

jbyteArray bytes(JNIEnv* env, const char* s) { return bytes(env, s, s ? std::strlen(s) : 0); }

jintArray ints(JNIEnv* env, const std::vector<jint>& v) {
  jintArray a = env->NewIntArray(static_cast<jsize>(v.size()));
  if (a && !v.empty()) env->SetIntArrayRegion(a, 0, static_cast<jsize>(v.size()), v.data());
  return a;
}

jfloatArray floats(JNIEnv* env, const float* p, size_t n) {
  jfloatArray a = env->NewFloatArray(static_cast<jsize>(n));
  if (a && n) env->SetFloatArrayRegion(a, 0, static_cast<jsize>(n), p);
  return a;
}

}  // namespace

#define FN(ret, name) extern "C" JNIEXPORT ret JNICALL Java_com_muhenkit_app_cad_MkNative_##name

FN(jlong, open)(JNIEnv* env, jobject, jbyteArray data) {
  jsize n = env->GetArrayLength(data);
  std::vector<uint8_t> buf(static_cast<size_t>(n));
  env->GetByteArrayRegion(data, 0, n, reinterpret_cast<jbyte*>(buf.data()));
  return reinterpret_cast<jlong>(mk_open(buf.data(), n));
}

FN(jbyteArray, lastError)(JNIEnv* env, jobject) { return bytes(env, mk_last_error()); }
FN(void, close)(JNIEnv*, jobject, jlong h) { mk_close(D(h)); }
FN(jint, buildScene)(JNIEnv*, jobject, jlong h) { return mk_build_scene(D(h)); }

/// "sürüm|kod sayfası|nesne|blok|tür istatistiği"
FN(jbyteArray, info)(JNIEnv* env, jobject, jlong h) {
  std::string s = std::string(mk_dxf_version(D(h))) + "|" + mk_codepage(D(h)) + "|" + std::to_string(mk_entity_count(D(h))) +
                  "|" + std::to_string(mk_block_count(D(h))) + "|" + mk_type_stats(D(h));
  return bytes(env, s.data(), s.size());
}

FN(jfloatArray, extents)(JNIEnv* env, jobject, jlong h) {
  float e[4];
  mk_extents(D(h), e);
  return floats(env, e, 4);
}

FN(jfloatArray, vertices)(JNIEnv* env, jobject, jlong h) {
  return floats(env, mk_vertices(D(h)), static_cast<size_t>(mk_vertex_float_count(D(h))));
}

/// Her parti: katman, rgb, float başlangıcı, float sayısı
FN(jintArray, batches)(JNIEnv* env, jobject, jlong h) {
  int n = mk_batch_count(D(h));
  const MkBatch* b = mk_batches(D(h));
  std::vector<jint> v;
  v.reserve(n * 4);
  for (int i = 0; i < n; i++) {
    v.push_back(b[i].layer);
    v.push_back(static_cast<jint>(b[i].rgb));
    v.push_back(static_cast<jint>(b[i].offset));
    v.push_back(static_cast<jint>(b[i].count));
  }
  return ints(env, v);
}

/// Her poligon: katman, rgb, grup, nokta sayısı, float başlangıcı, alfa*1000
FN(jintArray, polys)(JNIEnv* env, jobject, jlong h) {
  int n = mk_poly_count(D(h));
  const MkPoly* p = mk_polys(D(h));
  std::vector<jint> v;
  v.reserve(n * 6);
  for (int i = 0; i < n; i++) {
    v.push_back(p[i].layer);
    v.push_back(static_cast<jint>(p[i].rgb));
    v.push_back(p[i].group);
    v.push_back(p[i].npoints);
    v.push_back(static_cast<jint>(p[i].offset));
    v.push_back(static_cast<jint>(p[i].alpha * 1000.0f + 0.5f));
  }
  return ints(env, v);
}

/// Her yazı 6 float: x, y, yükseklik, dönüş, genişlik çarpanı, kutu genişliği
FN(jfloatArray, textFloats)(JNIEnv* env, jobject, jlong h) {
  int n = mk_text_count(D(h));
  const MkText* t = mk_texts(D(h));
  std::vector<float> v;
  v.reserve(n * 6);
  for (int i = 0; i < n; i++) {
    v.insert(v.end(), {t[i].x, t[i].y, t[i].height, t[i].rotation, t[i].width_factor, t[i].box_width});
  }
  return floats(env, v.data(), v.size());
}

/// Her yazı 6 int: katman, rgb, yatay hiza, dikey hiza, havuz başlangıcı, bayt uzunluğu
FN(jintArray, textInts)(JNIEnv* env, jobject, jlong h) {
  int n = mk_text_count(D(h));
  const MkText* t = mk_texts(D(h));
  std::vector<jint> v;
  v.reserve(n * 6);
  for (int i = 0; i < n; i++) {
    v.insert(v.end(), {t[i].layer, static_cast<jint>(t[i].rgb), t[i].halign, t[i].valign, t[i].utf8_offset, t[i].utf8_len});
  }
  return ints(env, v);
}

FN(jbyteArray, textPool)(JNIEnv* env, jobject, jlong h) {
  int n = mk_text_count(D(h));
  const MkText* t = mk_texts(D(h));
  size_t len = 0;
  for (int i = 0; i < n; i++) len = std::max(len, static_cast<size_t>(t[i].utf8_offset) + t[i].utf8_len);
  return bytes(env, mk_text_pool(D(h)), len);
}

/// Katman adları, '\0' ile ayrılmış UTF-8
FN(jbyteArray, layerNames)(JNIEnv* env, jobject, jlong h) {
  std::string s;
  int n = mk_layer_count(D(h));
  for (int i = 0; i < n; i++) {
    if (i) s.push_back('\0');
    s += mk_layer_name(D(h), i);
  }
  return bytes(env, s.data(), s.size());
}

/// Her katman: rgb, bayraklar (1 kapalı, 2 dondurulmuş), nesne sayısı
FN(jintArray, layerInts)(JNIEnv* env, jobject, jlong h) {
  int n = mk_layer_count(D(h));
  std::vector<jint> v;
  v.reserve(n * 3);
  for (int i = 0; i < n; i++) {
    v.push_back(static_cast<jint>(mk_layer_rgb(D(h), i)));
    v.push_back(mk_layer_flags(D(h), i));
    v.push_back(mk_layer_entity_count(D(h), i));
  }
  return ints(env, v);
}

// ───────────────────────── düzenleme ─────────────────────────

namespace {

std::vector<int32_t> idList(JNIEnv* env, jintArray ids) {
  jsize n = env->GetArrayLength(ids);
  std::vector<int32_t> v(static_cast<size_t>(n));
  if (n) env->GetIntArrayRegion(ids, 0, n, v.data());
  return v;
}

std::string utf8(JNIEnv* env, jbyteArray a) {
  jsize n = env->GetArrayLength(a);
  std::string s(static_cast<size_t>(n), '\0');
  if (n) env->GetByteArrayRegion(a, 0, n, reinterpret_cast<jbyte*>(&s[0]));
  return s;
}

}  // namespace

FN(jint, pick)(JNIEnv*, jobject, jlong h, jdouble x, jdouble y, jdouble tol) { return mk_pick(D(h), x, y, tol); }

/// [tür, x, y] — tür 0 ise yakalama yok
FN(jdoubleArray, snap)(JNIEnv* env, jobject, jlong h, jdouble x, jdouble y, jdouble tol) {
  double out[2] = {x, y};
  int t = mk_snap(D(h), x, y, tol, out);
  double r[3] = {static_cast<double>(t), out[0], out[1]};
  jdoubleArray a = env->NewDoubleArray(3);
  env->SetDoubleArrayRegion(a, 0, 3, r);
  return a;
}

FN(jfloatArray, geometry)(JNIEnv* env, jobject, jlong h, jint id) {
  int64_t n = 0;
  const float* p = mk_entity_geometry(D(h), id, &n);
  return floats(env, p, static_cast<size_t>(n));
}

FN(jbyteArray, entityInfo)(JNIEnv* env, jobject, jlong h, jint id) { return bytes(env, mk_entity_info(D(h), id)); }

FN(jint, move)(JNIEnv* env, jobject, jlong h, jintArray ids, jdouble dx, jdouble dy) {
  auto v = idList(env, ids);
  return mk_move(D(h), v.data(), static_cast<int32_t>(v.size()), dx, dy);
}

FN(jint, copy)(JNIEnv* env, jobject, jlong h, jintArray ids, jdouble dx, jdouble dy) {
  auto v = idList(env, ids);
  return mk_copy(D(h), v.data(), static_cast<int32_t>(v.size()), dx, dy);
}

FN(jint, remove)(JNIEnv* env, jobject, jlong h, jintArray ids) {
  auto v = idList(env, ids);
  return mk_delete(D(h), v.data(), static_cast<int32_t>(v.size()));
}

FN(jint, addLine)(JNIEnv* env, jobject, jlong h, jbyteArray layer, jdouble x1, jdouble y1, jdouble x2, jdouble y2) {
  return mk_add_line(D(h), utf8(env, layer).c_str(), x1, y1, x2, y2);
}

FN(jint, addText)(JNIEnv* env, jobject, jlong h, jbyteArray layer, jdouble x, jdouble y, jdouble height, jdouble rot,
                  jbyteArray text) {
  return mk_add_text(D(h), utf8(env, layer).c_str(), x, y, height, rot, utf8(env, text).c_str());
}

FN(jint, undo)(JNIEnv*, jobject, jlong h) { return mk_undo(D(h)); }
FN(jint, redo)(JNIEnv*, jobject, jlong h) { return mk_redo(D(h)); }
FN(jint, undoCount)(JNIEnv*, jobject, jlong h) { return mk_undo_count(D(h)); }
FN(jint, redoCount)(JNIEnv*, jobject, jlong h) { return mk_redo_count(D(h)); }
FN(jint, isDirty)(JNIEnv*, jobject, jlong h) { return mk_is_dirty(D(h)); }

FN(jbyteArray, save)(JNIEnv* env, jobject, jlong h) {
  int64_t n = 0;
  const uint8_t* p = mk_save(D(h), &n);
  return bytes(env, reinterpret_cast<const char*>(p), static_cast<size_t>(n));
}

FN(jintArray, selectRect)(JNIEnv* env, jobject, jlong h, jdouble x0, jdouble y0, jdouble x1, jdouble y1, jboolean crossing) {
  int cap = mk_entity_count(D(h));
  std::vector<int32_t> out(static_cast<size_t>(cap > 0 ? cap : 1));
  int n = mk_select_rect(D(h), x0, y0, x1, y1, crossing ? 1 : 0, out.data(), cap);
  out.resize(static_cast<size_t>(std::min(n, cap)));
  return ints(env, std::vector<jint>(out.begin(), out.end()));
}

// ───────────────────────── dönüşüm ve özellikler ─────────────────────────

FN(jint, rotate)(JNIEnv* env, jobject, jlong h, jintArray ids, jdouble cx, jdouble cy, jdouble deg) {
  auto v = idList(env, ids);
  return mk_rotate(D(h), v.data(), static_cast<int32_t>(v.size()), cx, cy, deg);
}

FN(jint, scale)(JNIEnv* env, jobject, jlong h, jintArray ids, jdouble cx, jdouble cy, jdouble k) {
  auto v = idList(env, ids);
  return mk_scale(D(h), v.data(), static_cast<int32_t>(v.size()), cx, cy, k);
}

FN(jint, mirror)(JNIEnv* env, jobject, jlong h, jintArray ids, jdouble x1, jdouble y1, jdouble x2, jdouble y2, jboolean keep) {
  auto v = idList(env, ids);
  return mk_mirror(D(h), v.data(), static_cast<int32_t>(v.size()), x1, y1, x2, y2, keep ? 1 : 0);
}

FN(jbyteArray, entityText)(JNIEnv* env, jobject, jlong h, jint id) { return bytes(env, mk_entity_text(D(h), id)); }

FN(jint, setText)(JNIEnv* env, jobject, jlong h, jint id, jbyteArray text) {
  return mk_set_text(D(h), id, utf8(env, text).c_str());
}

FN(jint, setLayer)(JNIEnv* env, jobject, jlong h, jintArray ids, jbyteArray layer) {
  auto v = idList(env, ids);
  return mk_set_layer(D(h), v.data(), static_cast<int32_t>(v.size()), utf8(env, layer).c_str());
}

FN(jint, setColor)(JNIEnv* env, jobject, jlong h, jintArray ids, jint aci) {
  auto v = idList(env, ids);
  return mk_set_color(D(h), v.data(), static_cast<int32_t>(v.size()), aci);
}

// ───────────────────────── çizim: daire, çoklu çizgi, ötele ─────────────────────────

FN(jint, addCircle)(JNIEnv* env, jobject, jlong h, jbyteArray layer, jdouble cx, jdouble cy, jdouble r) {
  return mk_add_circle(D(h), utf8(env, layer).c_str(), cx, cy, r);
}

FN(jint, addPolyline)(JNIEnv* env, jobject, jlong h, jbyteArray layer, jdoubleArray xy, jboolean closed) {
  jsize n = env->GetArrayLength(xy);
  std::vector<double> v(static_cast<size_t>(n));
  if (n) env->GetDoubleArrayRegion(xy, 0, n, v.data());
  return mk_add_polyline(D(h), utf8(env, layer).c_str(), v.data(), n / 2, closed ? 1 : 0);
}

FN(jint, offset)(JNIEnv*, jobject, jlong h, jint id, jdouble d, jdouble px, jdouble py) {
  return mk_offset(D(h), id, d, px, py);
}

// ───────────────────────── semboller (bloklar) ─────────────────────────

FN(jint, defineBlock)(JNIEnv* env, jobject, jlong h, jbyteArray name, jbyteArray snippet) {
  return mk_define_block(D(h), utf8(env, name).c_str(), utf8(env, snippet).c_str());
}

FN(jint, insertBlock)(JNIEnv* env, jobject, jlong h, jbyteArray layer, jbyteArray name, jdouble x, jdouble y, jdouble s, jdouble rot) {
  return mk_insert_block(D(h), utf8(env, layer).c_str(), utf8(env, name).c_str(), x, y, s, rot);
}

FN(jbyteArray, insertCounts)(JNIEnv* env, jobject, jlong h) { return bytes(env, mk_insert_counts(D(h))); }

// ───────────────────────── grupla / çöz ─────────────────────────

FN(jint, group)(JNIEnv* env, jobject, jlong h, jintArray ids) {
  auto v = idList(env, ids);
  return mk_group(D(h), v.data(), static_cast<int32_t>(v.size()));
}

/// [ilk yeni kimlik, adet]
FN(jintArray, explode)(JNIEnv* env, jobject, jlong h, jintArray ids) {
  auto v = idList(env, ids);
  int32_t count = 0;
  int32_t first = mk_explode(D(h), v.data(), static_cast<int32_t>(v.size()), &count);
  return ints(env, {first, count});
}

// ───────────────────────── hesap çekirdeği (mkcalc) ─────────────────────────

extern "C" JNIEXPORT jbyteArray JNICALL Java_com_muhenkit_app_calc_CalcNative_calc(JNIEnv* env, jobject, jbyteArray module, jbyteArray input) {
  return bytes(env, mc_calc(utf8(env, module).c_str(), utf8(env, input).c_str()));
}

extern "C" JNIEXPORT jbyteArray JNICALL Java_com_muhenkit_app_calc_CalcNative_table(JNIEnv* env, jobject, jbyteArray name) {
  return bytes(env, mc_table(utf8(env, name).c_str()));
}

// ───────────────────────── proje otomatik kaydı ─────────────────────────

FN(jbyteArray, saveCopy)(JNIEnv* env, jobject, jlong h) {
  int64_t n = 0;
  const uint8_t* p = mk_save_copy(D(h), &n);
  return bytes(env, reinterpret_cast<const char*>(p), static_cast<size_t>(n));
}

FN(void, setDirty)(JNIEnv*, jobject, jlong h, jboolean dirty) { mk_set_dirty(D(h), dirty ? 1 : 0); }
