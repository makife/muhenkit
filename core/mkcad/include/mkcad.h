/*
 * mkcad — MühenKit DXF motoru (C++17), dışa açık C arayüzü.
 *
 * Akış:  mk_open(bayt) → mk_build_scene() → tamponları oku → mk_close()
 *
 * Koordinatlar dünya birimindedir (çizim birimi). Tüm metinler UTF-8'dir.
 * Döndürülen işaretçiler belge kapanana veya sahne yeniden kurulana kadar
 * geçerlidir; Dart tarafı bunları kopyalamadan (zero-copy) okuyabilir.
 */
#ifndef MKCAD_H
#define MKCAD_H

#include <stdint.h>

#if defined(_WIN32)
#define MK_API __declspec(dllexport)
#else
#define MK_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct MkDoc MkDoc;

/* Çizgi parçaları: kind = 0 → vertices[offset .. offset+count) içinde
 * (x1,y1,x2,y2) dörtlüleri.  Dolgu: kind = 1 → poligon listesi (bkz. MkPoly). */
typedef struct MkBatch {
  int32_t layer;   /* mk_layer_* indeksi */
  uint32_t rgb;    /* 0xRRGGBB */
  int32_t kind;    /* 0: çizgi parçaları */
  int32_t _pad;
  int64_t offset;  /* float indeksi */
  int64_t count;   /* float sayısı (4'ün katı) */
  float minx, miny, maxx, maxy;
} MkBatch;

/* Dolu poligon (SOLID, katı HATCH). Aynı group değerine sahip halkalar
 * tek bir yol olarak çift-tek (even-odd) kuralıyla doldurulur. */
typedef struct MkPoly {
  int32_t layer;
  uint32_t rgb;
  int32_t group;
  int32_t npoints;
  int64_t offset;  /* float indeksi, (x,y) çiftleri */
  float alpha;     /* 1: katı, <1: desenli taramanın yarı saydam gösterimi */
  int32_t _pad;
} MkPoly;

typedef struct MkText {
  float x, y;          /* hizalama noktası (dünya) */
  float height;        /* yazı yüksekliği (dünya) */
  float rotation;      /* derece, saat yönünün tersi */
  float width_factor;
  float box_width;     /* MTEXT kutu genişliği, yoksa 0 */
  int32_t layer;
  uint32_t rgb;
  int32_t halign;      /* 0 sol, 1 orta, 2 sağ */
  int32_t valign;      /* 0 taban, 1 alt, 2 orta, 3 üst */
  int32_t utf8_offset; /* mk_text_pool içinde */
  int32_t utf8_len;
} MkText;

MK_API const char* mk_version(void);

/* Belge açma. Hata olursa NULL döner, sebep mk_last_error() ile alınır. */
MK_API MkDoc* mk_open(const uint8_t* data, int64_t len);
MK_API const char* mk_last_error(void);
MK_API void mk_close(MkDoc* doc);

/* Belge bilgisi */
MK_API const char* mk_dxf_version(MkDoc* doc);   /* ör. "AC1015" */
MK_API const char* mk_codepage(MkDoc* doc);
MK_API int32_t mk_entity_count(MkDoc* doc);      /* model alanı */
MK_API int32_t mk_block_count(MkDoc* doc);
MK_API const char* mk_type_stats(MkDoc* doc);    /* "LINE=4225;TEXT=819;..." */

/* Katmanlar */
MK_API int32_t mk_layer_count(MkDoc* doc);
MK_API const char* mk_layer_name(MkDoc* doc, int32_t i);
MK_API uint32_t mk_layer_rgb(MkDoc* doc, int32_t i);
MK_API int32_t mk_layer_flags(MkDoc* doc, int32_t i);        /* bit0 kapalı, bit1 dondurulmuş */
MK_API int32_t mk_layer_entity_count(MkDoc* doc, int32_t i); /* sahnedeki ilkel sayısı */

/* Sahne: blokları açar, eğrileri parçalara böler, tamponları üretir. */
MK_API int32_t mk_build_scene(MkDoc* doc);
MK_API void mk_extents(MkDoc* doc, float* out4); /* minx, miny, maxx, maxy */

MK_API const float* mk_vertices(MkDoc* doc);
MK_API int64_t mk_vertex_float_count(MkDoc* doc);
MK_API int32_t mk_batch_count(MkDoc* doc);
MK_API const MkBatch* mk_batches(MkDoc* doc);
MK_API int32_t mk_poly_count(MkDoc* doc);
MK_API const MkPoly* mk_polys(MkDoc* doc);
MK_API int32_t mk_text_count(MkDoc* doc);
MK_API const MkText* mk_texts(MkDoc* doc);
MK_API const char* mk_text_pool(MkDoc* doc);

/* ───────────── Düzenleme (2. aşama) ─────────────
 * Nesne kimliği = model alanındaki üst düzey nesne indeksi (silinse de sabit).
 * Her düzenlemeden sonra çağıran taraf mk_build_scene() ile sahneyi yeniler. */

/* (x,y) noktasına tol mesafesindeki en yakın nesne; yoksa -1. */
MK_API int32_t mk_pick(MkDoc* doc, double x, double y, double tol);
/* Yakalama: 0 yok, 1 uç, 2 orta, 3 merkez, 4 ekleme noktası; nokta out2'ye yazılır. */
MK_API int32_t mk_snap(MkDoc* doc, double x, double y, double tol, double* out2);
/* Seçim vurgusu için nesnenin dünya geometrisi (x1,y1,x2,y2 dörtlüleri). */
MK_API const float* mk_entity_geometry(MkDoc* doc, int32_t id, int64_t* float_count);
/* Pencere seçimi: crossing=0 tamamen içeridekiler, 1 değenler. out'a en çok cap kimlik yazılır; toplam döner. */
MK_API int32_t mk_select_rect(MkDoc* doc, double x0, double y0, double x1, double y1, int32_t crossing, int32_t* out,
                              int32_t cap);
/* Nesne bilgisi: "TÜR|katman|handle" (UTF-8). */
MK_API const char* mk_entity_info(MkDoc* doc, int32_t id);

MK_API int32_t mk_move(MkDoc* doc, const int32_t* ids, int32_t n, double dx, double dy);  /* taşınan adet */
MK_API int32_t mk_copy(MkDoc* doc, const int32_t* ids, int32_t n, double dx, double dy);  /* ilk yeni kimlik veya -1 */
MK_API int32_t mk_delete(MkDoc* doc, const int32_t* ids, int32_t n);
MK_API int32_t mk_add_line(MkDoc* doc, const char* layer, double x1, double y1, double x2, double y2);
MK_API int32_t mk_add_text(MkDoc* doc, const char* layer, double x, double y, double height, double rotation,
                           const char* utf8);

/* Çizim: daire; çoklu çizgi (xy: n nokta çifti, closed: kapalı). R12 dosyalarında çoklu çizgi
 * ayrı çizgilere dönüşür. Dönen: ilk yeni kimlik veya -1. */
MK_API int32_t mk_add_circle(MkDoc* doc, const char* layer, double cx, double cy, double r);
MK_API int32_t mk_add_polyline(MkDoc* doc, const char* layer, const double* xy, int32_t n, int32_t closed);
/* Ötele: (px,py) tarafında d mesafede paralel kopya. Yeni kimlik; desteklenmiyorsa -1. */
MK_API int32_t mk_offset(MkDoc* doc, int32_t id, double d, double px, double py);

/* Blok (sembol) tanımı: snippet "kod
değer
…" biçiminde, blok yerel koordinatlarında nesneler.
 * Aynı adlı blok varsa dokunmaz. 1: tamam, 0: hata (mk_last_error). Geri alınamaz; kullanılmayan tanım zararsızdır. */
MK_API int32_t mk_define_block(MkDoc* doc, const char* name, const char* snippet);
/* Blok yerleştir (INSERT). Yeni kimlik veya -1. */
MK_API int32_t mk_insert_block(MkDoc* doc, const char* layer, const char* name, double x, double y, double scale,
                               double rotation);
/* Çizimdeki blok yerleştirme sayıları: "ad	adet" satırları */
MK_API const char* mk_insert_counts(MkDoc* doc);

/* Grupla: seçimi tek bir bloğa çevirir (taban = seçim merkezi). Yeni yerleştirmenin kimliği veya -1. */
MK_API int32_t mk_group(MkDoc* doc, const int32_t* ids, int32_t n);
/* Çöz: blok yerleştirmelerini parçalarına ayırır. Yeni nesneler ardışık; ilk kimlik, toplam *count'a. -1: hiçbiri. */
MK_API int32_t mk_explode(MkDoc* doc, const int32_t* ids, int32_t n, int32_t* count);

/* Dönüşümler (seçili nesneler). mk_mirror: keep_original=1 ise aynalı kopya üretir, ilk yeni kimliği döner. */
MK_API int32_t mk_rotate(MkDoc* doc, const int32_t* ids, int32_t n, double cx, double cy, double degrees);
MK_API int32_t mk_scale(MkDoc* doc, const int32_t* ids, int32_t n, double cx, double cy, double factor);
MK_API int32_t mk_mirror(MkDoc* doc, const int32_t* ids, int32_t n, double x1, double y1, double x2, double y2,
                         int32_t keep_original);
/* Yazı içeriği (TEXT/MTEXT/ATTRIB), biçim kodları temizlenmiş UTF-8; yazı değilse "". */
MK_API const char* mk_entity_text(MkDoc* doc, int32_t id);
MK_API int32_t mk_set_text(MkDoc* doc, int32_t id, const char* utf8);
MK_API int32_t mk_set_layer(MkDoc* doc, const int32_t* ids, int32_t n, const char* layer);
/* aci: 1–255, 256 = DAMARDAN (katman rengi), 0 = BLOKTAN */
MK_API int32_t mk_set_color(MkDoc* doc, const int32_t* ids, int32_t n, int32_t aci);

MK_API int32_t mk_undo(MkDoc* doc);
MK_API int32_t mk_redo(MkDoc* doc);
MK_API int32_t mk_undo_count(MkDoc* doc);
MK_API int32_t mk_redo_count(MkDoc* doc);
MK_API int32_t mk_is_dirty(MkDoc* doc);

/* DXF olarak kaydet; dönen tampon belge kapanana/yeniden kaydedilene kadar geçerli. */
MK_API const uint8_t* mk_save(MkDoc* doc, int64_t* len);
/* mk_save gibi, ama "kaydedilmedi" işaretine dokunmaz (proje otomatik kaydı için). */
MK_API const uint8_t* mk_save_copy(MkDoc* doc, int64_t* len);
/* Proje yeniden açılınca "kaynak dosyaya kaydedilmemiş değişiklik var" durumunu geri yükler. */
MK_API void mk_set_dirty(MkDoc* doc, int32_t dirty);

#ifdef __cplusplus
}
#endif

#endif /* MKCAD_H */
