/* mkcalc: MühenKit hesap çekirdeği (Android, Windows, iOS ortak).
 *
 * Arayüz bilinçli olarak metin tabanlıdır: her platform yalnızca iki işlevi bağlar.
 *   girdi / çıktı: "anahtar=değer" satırları (UTF-8). Dizi elemanları "ad.0.alan=…".
 *   Sayılar nokta ondalıklıdır. Hata olursa çıktıda "error=…" satırı bulunur.
 *
 * Modüller: lighting, voltage_drop, diversity, grounding, lightning, motor, cable,
 *           meter_panel, load_schedule
 * Tablolar: cables, nyy, fuses, motor, diversity, combos, utilization, soiling, luminaires,
 *           meter_panel, conductors, lp_c1 … lp_c5, lux, prices  (sekme ayrılmış satırlar)
 */
#ifndef MKCALC_H
#define MKCALC_H

#if defined(_WIN32)
#define MC_API __declspec(dllexport)
#else
#define MC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Hesap: dönen tampon aynı iş parçacığında bir sonraki çağrıya kadar geçerli. */
MC_API const char* mc_calc(const char* module, const char* input);

/* Veri tablosu (sekme ayrılmış; ilk satır başlıklar). */
MC_API const char* mc_table(const char* name);

#ifdef __cplusplus
}
#endif

#endif /* MKCALC_H */
