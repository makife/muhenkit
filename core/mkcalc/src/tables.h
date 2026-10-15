// Hesap tabloları. Kaynaklar kullanıcının Excel/görsel dosyalarıdır (eski uygulamadan birebir):
// aydinlatma tablolar hesap-2.xls, Yükleme cetveli xls (ATK), kablo.jpg, ES+ZAMANLILIK.png,
// TMS SEÇİM CETVELİ.gif, SAYAC APNOSU.xls, paratoner hesap programı, Topraklama hesabı xls.
#pragma once

namespace mc {

struct Cable {
  const char* name;  // 4x2,5
  double section;    // faz iletkeni mm²
  double ampacity;   // hesapta kullanılan akım taşıma (A)
  double toprak;     // NYY toprakta (A)
  double hava;       // NYY havada (A)
  double r, x;       // Ω/km
};

inline const Cable kCables[] = {
    {"4x1,5", 1.5, 18, 27, 18, 12.1, 0.09},        {"4x2,5", 2.5, 32, 36, 25, 7.41, 0.09},
    {"4x4", 4, 42, 47, 34, 4.61, 0.082},           {"4x6", 6, 54, 58, 44, 3.08, 0.082},
    {"4x10", 10, 75, 77, 60, 1.83, 0.082},         {"4x16", 16, 100, 100, 80, 1.15, 0.082},
    {"3x25+16", 25, 127, 130, 105, 0.727, 0.082},  {"3x35+16", 35, 158, 155, 130, 0.524, 0.079},
    {"3x50+25", 50, 192, 185, 160, 0.387, 0.078},  {"3x70+35", 70, 246, 230, 200, 0.268, 0.075},
    {"3x95+50", 95, 298, 275, 245, 0.193, 0.075},  {"3x120+70", 120, 346, 315, 285, 0.153, 0.073},
    {"3x150+70", 150, 399, 355, 325, 0.124, 0.073}, {"3x185+95", 185, 456, 400, 370, 0.0991, 0.072},
    {"3x240+120", 240, 538, 460, 435, 0.0754, 0.072}, {"3x300+150", 300, 500, 520, 500, 0.0601, 0.071},
};
inline constexpr int kCableCount = sizeof(kCables) / sizeof(kCables[0]);

inline const int kFuses[] = {6, 10, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 320, 400, 500, 630, 800, 1000, 1250};
inline constexpr int kFuseCount = sizeof(kFuses) / sizeof(kFuses[0]);

// [min daire, max daire, katsayı %]
inline const int kBuildingDiversity[][3] = {
    {3, 5, 45},   {6, 10, 43},  {11, 15, 41}, {16, 20, 39}, {21, 25, 36}, {26, 30, 34}, {31, 35, 31},
    {36, 40, 29}, {41, 45, 28}, {46, 50, 26}, {51, 55, 25}, {56, 61, 24}, {62, 99999, 23},
};
inline constexpr int kDiversityCount = sizeof(kBuildingDiversity) / sizeof(kBuildingDiversity[0]);

inline const char* kCombos[] = {"80-50-30", "80-50-10", "80-30-30", "80-30-10", "50-50-30",
                                "50-50-10", "50-30-30", "50-30-10", "30-30-30", "10-10-10"};
inline constexpr int kComboCount = 10;
inline const double kRoomIndexK[] = {0.6, 0.8, 1.0, 1.25, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0};
inline constexpr int kKCount = 10;
inline const double kUtilization[10][10] = {
    {0.24, 0.23, 0.18, 0.18, 0.20, 0.19, 0.15, 0.15, 0.12, 0.15},
    {0.31, 0.29, 0.24, 0.23, 0.25, 0.24, 0.20, 0.19, 0.16, 0.17},
    {0.36, 0.33, 0.29, 0.28, 0.29, 0.28, 0.24, 0.23, 0.20, 0.20},
    {0.41, 0.38, 0.34, 0.32, 0.33, 0.31, 0.28, 0.27, 0.24, 0.24},
    {0.45, 0.41, 0.38, 0.36, 0.36, 0.34, 0.32, 0.30, 0.27, 0.26},
    {0.51, 0.46, 0.45, 0.41, 0.41, 0.38, 0.37, 0.35, 0.31, 0.30},
    {0.56, 0.49, 0.50, 0.45, 0.45, 0.41, 0.41, 0.38, 0.35, 0.34},
    {0.59, 0.52, 0.54, 0.48, 0.47, 0.43, 0.43, 0.40, 0.38, 0.36},
    {0.63, 0.55, 0.58, 0.51, 0.50, 0.46, 0.47, 0.44, 0.41, 0.39},
    {0.66, 0.57, 0.62, 0.54, 0.53, 0.48, 0.50, 0.46, 0.44, 0.40},
};

struct Named {
  const char* name;
  double value;
};
inline const Named kSoiling[] = {{"Az kirli · 1,25", 1.25}, {"Normal · 1,40", 1.40}, {"Normal+ · 1,55", 1.55}, {"Çok kirli · 1,70", 1.70}};
inline const Named kLuminaires[] = {
    {"LED Panel 60x60 40W", 3600}, {"TMS 4x18W", 5200},       {"TMS 2x36W", 6400},          {"TMS 4x14W T5", 4800},
    {"TMS 2x18W", 2600},           {"Floresan 1x40W", 3200},  {"Floresan 1x36W", 2050},     {"Akkor 100W", 1380},
    {"Akkor 60W", 730},            {"Civa buharlı 125W", 5600}, {"Civa buharlı 250W", 12000}, {"Civa buharlı 400W", 21000},
};
// Gömülü iletken eşdeğer çapları (m)
inline const Named kConductors[] = {
    {"16 mm² dolu", 0.0045},    {"25 mm² dolu", 0.006},     {"35 mm² dolu", 0.0067},    {"50 mm² dolu", 0.008},
    {"50 mm² örgülü", 0.0095},  {"70 mm² örgülü", 0.011},   {"95 mm² örgülü", 0.0126},  {"120 mm² örgülü", 0.014},
    {"150 mm² örgülü", 0.016},  {"185 mm² örgülü", 0.0175}, {"240 mm² örgülü", 0.0203},
};
inline const Named kLpC1[] = {{"Yüksek yapılarla çevrili", 0.25}, {"Aynı / daha alçak yapılarla çevrili", 0.5},
                              {"Yalıtılmış (çevresinde yapı yok)", 1}, {"Tepe / yükselti üzerinde yalnız", 2}};
inline const Named kLpC2[] = {{"Metal yapı · metal çatı", 0.5}, {"Metal yapı · normal çatı", 1}, {"Normal yapı · normal çatı", 1},
                              {"Normal yapı · yanıcı çatı", 2.5}, {"Yanıcı yapı · yanıcı çatı", 3}};
inline const Named kLpC3[] = {{"Değersiz, yanıcı olmayan", 0.5}, {"Normal değerde / normal yanıcı", 1},
                              {"Yüksek değerli / yanıcı", 2}, {"Paha biçilmez / patlayıcı", 3}};
inline const Named kLpC4[] = {{"Boş yapı", 0.5}, {"Normal kullanım", 1}, {"Tahliyesi zor / panik riski", 3}};
inline const Named kLpC5[] = {{"Hizmet sürekliliği gerekmez", 1}, {"Hizmet sürekliliği gerekli", 5}, {"Çevreye etkisi var", 10}};

/// Koruma seviyesi → ESE yuvarlanan küre D (m)
inline double lpLevelD(int level) { return level == 1 ? 20 : level == 2 ? 45 : 60; }

struct MotorRow {
  double kw;
  const char* hp;
  double amps, kesit;
  const char* termik;
  const char* busonNormal;
  const char* busonGecikmeli;
  const char* nh;
};
inline const MotorRow kMotor[] = {
    {0.06, "1/12", 0.22, 2.5, "0,19-0,29", "0,8", "-", "-"},   {0.09, "1/8", 0.32, 2.5, "0,27-0,4", "1,25", "-", "-"},
    {0.12, "1/6", 0.44, 2.5, "0,37-0,55", "2", "2", "-"},       {0.18, "1/4", 0.61, 2.5, "0,50-0,75", "2", "2", "-"},
    {0.25, "1/3", 0.78, 2.5, "0,67-1", "2-4", "2", "-"},        {0.37, "1/2", 1.12, 2.5, "0,9-1,3", "4-6", "4", "-"},
    {0.55, "3/4", 1.47, 2.5, "1,2-1,8", "4-6", "4-6", "-"},     {0.75, "1", 1.95, 2.5, "1,6-2,4", "4-10", "4-6", "6"},
    {1.1, "1,5", 2.85, 2.5, "2,2-3,3", "10", "6", "6"},         {1.5, "2", 3.8, 2.5, "3-4,5", "10-20", "10", "10"},
    {2.2, "3", 5.4, 2.5, "4-6", "16-20", "10-16", "10-16"},     {3, "4", 7.1, 2.5, "5,3-8", "16-21", "16", "16"},
    {4, "5,5", 8.8, 2.5, "7,3-9", "20", "16", "16"},            {5.5, "7,5", 11.7, 4, "8-12", "25-35", "20-25", "20-25"},
    {7.5, "10", 15.6, 6, "11-16", "35", "25", "25"},            {11, "15", 22, 10, "12-24", "50-63", "35-50", "35-50"},
    {15, "20", 29, 10, "20-32", "63", "50", "50"},              {18.5, "25", 37.5, 16, "24-45", "-", "-", "63-80"},
    {22, "30", 43.5, 16, "24-45", "-", "-", "63-80"},           {30, "40", 58, 25, "32-63", "-", "-", "80-100"},
    {37, "50", 70, 50, "50-90", "-", "-", "100-160"},           {45, "60", 85, 50, "70-110", "-", "-", "125-160"},
    {55, "75", 104, 50, "70-110", "-", "-", "160"},             {75, "100", 140, 120, "120-155", "-", "-", "200-250"},
    {90, "125", 168, 120, "140-170", "-", "-", "224-250"},      {110, "150", 205, 120, "150-300", "-", "-", "250-315"},
    {132, "180", 245, 120, "150-300", "-", "-", "315"},         {160, "220", 290, 185, "150-300", "-", "-", "355-400"},
    {200, "270", 360, 185, "200-400", "-", "-", "425-500"},     {250, "340", 450, 300, "315-630", "-", "-", "500-630"},
    {315, "430", 570, 300, "315-630", "-", "-", "630"},
};
inline constexpr int kMotorCount = sizeof(kMotor) / sizeof(kMotor[0]);

// SAYAC APNOSU.xls Çizelge 1: [bir kattaki sayaç, tek: 2kat,3kat,4kat toplam, tek pano eni, tek ŞekilA en, tek ŞekilB der,
//   üç: 2kat,3kat toplam, üç pano eni, üç ŞekilA en, üç ŞekilB der]
inline const double kMeterPanel[][12] = {
    {2, 3, 5, 7, 85, 105, 75, 4, 6, 100, 120, 75},          {3, 5, 8, 11, 100, 120, 75, 6, 9, 122.5, 142.5, 98},
    {4, 7, 11, 15, 115, 135, 90, 8, 12, 145, 165, 98},      {5, 9, 14, 19, 130, 150, 90, 10, 15, 167.5, 187.5, 120},
    {6, 11, 17, 23, 145, 165, 105, 12, 18, 190, 210, 120},  {7, 13, 20, 27, 160, 180, 105, 14, 21, 212.5, 232.5, 143},
    {8, 15, 23, 31, 175, 195, 120, 16, 24, 235, 255, 143},  {9, 17, 26, 35, 190, 210, 120, 18, 27, 257.5, 277.5, 165},
    {10, 19, 29, 39, 205, 225, 135, 20, 30, 280, 300, 165}, {11, 21, 32, 43, 220, 240, 135, 22, 33, 302.5, 322.5, 188},
    {12, 23, 35, 47, 235, 255, 150, 24, 36, 325, 345, 188}, {13, 25, 38, 51, 250, 270, 150, 26, 39, 347.5, 367.5, 210},
    {14, 27, 41, 55, 265, 285, 165, 28, 42, 370, 390, 210}, {15, 29, 44, 59, 280, 300, 165, 30, 45, 392.5, 412.5, 234},
    {16, 31, 47, 63, 295, 315, 180, 32, 48, 415, 435, 234}, {17, 33, 50, 67, 310, 330, 180, 34, 51, 437.5, 457.5, 255},
    {18, 35, 53, 71, 325, 345, 195, 36, 54, 460, 480, 255}, {19, 37, 56, 75, 340, 360, 195, 38, 57, 482.5, 502.5, 278},
    {20, 39, 59, 79, 355, 375, 210, 40, 60, 505, 525, 278},
};
inline constexpr int kMeterPanelCount = sizeof(kMeterPanel) / sizeof(kMeterPanel[0]);

// NYY (YVV) 0,6/1 kV kablo kataloğu (TS 212): kesit, direnç Ω/km, akım toprakta / havada (A), ağırlık kg/km.
// Kaynak: kablo.jpg (teknik bilgiler tablosu). Görselde 4x2,5 yalıtkan "10.9" ve 4x4 iletken çapı "1.26"
// okunuyordu; bu sütunlar burada kullanılmıyor.
struct Nyy {
  const char* name;
  double r, toprak, hava, kg;
};
inline const Nyy kNyy[] = {
    {"1x1,5", 11.9, 37, 26, 65},      {"1x2,5", 7.14, 50, 35, 80},       {"1x4", 4.47, 65, 46, 110},
    {"1x6", 2.97, 83, 58, 140},       {"1x10", 1.79, 110, 80, 195},      {"1x16", 1.12, 145, 105, 270},
    {"1x25", 0.712, 190, 140, 370},   {"1x35", 0.514, 235, 175, 480},    {"1x50", 0.379, 280, 215, 640},
    {"1x70", 0.262, 350, 270, 850},   {"1x95", 0.189, 420, 335, 1115},   {"1x120", 0.150, 480, 390, 1340},
    {"1x150", 0.122, 540, 445, 1660}, {"1x185", 0.0972, 620, 510, 2030}, {"1x240", 0.0740, 770, 620, 2650},
    {"1x300", 0.0590, 820, 710, 3370}, {"2x1,5", 12.1, 30, 21, 170},     {"2x2,5", 7.28, 41, 27, 220},
    {"2x4", 4.56, 53, 38, 290},       {"2x6", 3.03, 66, 48, 350},        {"2x10", 1.83, 88, 66, 480},
    {"3x1,5", 12.1, 27, 18, 190},     {"3x2,5", 7.28, 36, 25, 260},      {"3x4", 4.56, 46, 34, 340},
    {"3x6", 3.03, 58, 44, 420},       {"3x10", 1.89, 77, 60, 580},       {"4x1,5", 12.1, 27, 18, 230},
    {"4x2,5", 7.28, 36, 25, 300},     {"4x4", 4.56, 46, 34, 410},        {"4x6", 3.03, 58, 44, 510},
    {"4x10", 1.83, 77, 60, 780},      {"4x16", 1.15, 100, 80, 1100},     {"3x25/16", 0.727, 130, 105, 1420},
    {"3x35/16", 0.524, 155, 130, 1790}, {"3x50/25", 0.387, 185, 160, 2290}, {"3x70/35", 0.268, 230, 200, 3066},
    {"3x95/50", 0.193, 275, 245, 4097}, {"3x120/70", 0.153, 315, 285, 5700}, {"3x150/70", 0.124, 355, 325, 6132},
    {"3x185/95", 0.0991, 400, 370, 7625}, {"3x240/120", 0.0754, 460, 435, 9950}, {"3x300/150", 0.0601, 520, 500, 12500},
};

// lux.inc ve prices.inc eski uygulamanın veri dosyalarından otomatik üretilir (tools/gen_data.py)
struct LuxItem {
  const char* category;
  const char* name;
  double genel;  // < 0: yok
  double ozel;   // < 0: yok
};
struct PriceItem {
  const char* group;
  const char* name;
  const char* ref;
  double price;
};
extern const LuxItem kLux[];
extern const int kLuxCount;
extern const PriceItem kPrices[];
extern const int kPriceCount;

}  // namespace mc
