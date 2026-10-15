// mkcalc: hesap modülleri. Formüller eski uygulamadan (lib/calc/formulas.dart ve modül
// ekranları) birebir taşınmıştır; tools/check_against_dart ile karşılaştırılarak doğrulanır.
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <map>
#include <sstream>
#include <string>
#include <vector>

#include "mkcalc.h"
#include "tables.h"

namespace mc {
namespace {

constexpr double kPi = 3.14159265358979323846;

// ───────────────────────── girdi / çıktı ─────────────────────────

class In {
 public:
  explicit In(const char* s) {
    std::istringstream is(s ? s : "");
    std::string line;
    while (std::getline(is, line)) {
      if (!line.empty() && line.back() == '\r') line.pop_back();
      size_t eq = line.find('=');
      if (eq == std::string::npos) continue;
      kv_[line.substr(0, eq)] = line.substr(eq + 1);
    }
  }
  bool has(const std::string& k) const { return kv_.count(k) > 0; }
  double d(const std::string& k, double def) const {
    auto it = kv_.find(k);
    if (it == kv_.end() || it->second.empty()) return def;
    std::string v = it->second;
    std::replace(v.begin(), v.end(), ',', '.');
    char* end = nullptr;
    double x = std::strtod(v.c_str(), &end);
    return end == v.c_str() ? def : x;
  }
  int i(const std::string& k, int def) const { return static_cast<int>(std::lround(d(k, def))); }
  bool b(const std::string& k, bool def) const {
    auto it = kv_.find(k);
    if (it == kv_.end()) return def;
    return it->second == "1" || it->second == "true" || it->second == "evet";
  }
  std::string s(const std::string& k, const std::string& def = "") const {
    auto it = kv_.find(k);
    return it == kv_.end() ? def : it->second;
  }

 private:
  std::map<std::string, std::string> kv_;
};

class Out {
 public:
  void put(const std::string& k, double v) {
    char b[48];
    if (!std::isfinite(v)) {
      s_ += k + "=\n";
      return;
    }
    std::snprintf(b, sizeof(b), "%.10g", v);
    s_ += k + "=" + b + "\n";
  }
  void put(const std::string& k, int v) { s_ += k + "=" + std::to_string(v) + "\n"; }
  void put(const std::string& k, bool v) { s_ += k + "=" + (v ? "1" : "0") + "\n"; }
  void put(const std::string& k, const std::string& v) { s_ += k + "=" + v + "\n"; }
  void put(const std::string& k, const char* v) { s_ += k + "=" + std::string(v) + "\n"; }
  const std::string& str() const { return s_; }

 private:
  std::string s_;
};

/// Türkçe biçimli sayı (eski uygulamanın fmt'si): ondalık virgül, sondaki sıfırlar atılır
std::string fmt(double v, int digits) {
  char b[48];
  std::snprintf(b, sizeof(b), "%.*f", digits, v);
  std::string s = b;
  if (s.find('.') != std::string::npos) {
    while (!s.empty() && s.back() == '0') s.pop_back();
    if (!s.empty() && s.back() == '.') s.pop_back();
  }
  std::replace(s.begin(), s.end(), '.', ',');
  return s;
}

// ───────────────────────── ortak formüller (formulas.dart) ─────────────────────────

double roomIndex(double a, double b, double h) { return h <= 0 ? 0 : (a * b) / (h * (a + b)); }

double utilizationFor(double k, int combo) {
  combo = std::clamp(combo, 0, kComboCount - 1);
  if (k <= kRoomIndexK[0]) return kUtilization[0][combo];
  if (k >= kRoomIndexK[kKCount - 1]) return kUtilization[kKCount - 1][combo];
  for (int i = 0; i < kKCount - 1; i++) {
    double k0 = kRoomIndexK[i], k1 = kRoomIndexK[i + 1];
    if (k >= k0 && k <= k1) {
      double t = (k - k0) / (k1 - k0);
      return kUtilization[i][combo] + (kUtilization[i + 1][combo] - kUtilization[i][combo]) * t;
    }
  }
  return kUtilization[kKCount - 1][combo];
}

/// Konut daire eşzamanlılığı: 8 kW'a kadar %60, gerisi %40
double dwellingDemand(double w) { return w <= 8000 ? w * 0.6 : 8000 * 0.6 + (w - 8000) * 0.4; }

int buildingFactor(int flats) {
  if (flats < 3) return 100;
  for (const auto& r : kBuildingDiversity)
    if (flats >= r[0] && flats <= r[1]) return r[2];
  return 23;
}

double current3ph(double p, double u = 380, double cos = 0.8) { return p / (std::sqrt(3.0) * u * cos); }
double current1ph(double p, double u = 220, double cos = 1) { return p / (u * cos); }
double voltageDrop3ph(double p, double l, double s, double k = 56, double u = 380) { return s <= 0 ? 0 : 100 * l * p / (k * s * u * u); }
double voltageDrop1ph(double p, double l, double s, double k = 56, double u = 220) { return s <= 0 ? 0 : 200 * l * p / (k * s * u * u); }

int nextFuse(double amps) {
  for (int f : kFuses)
    if (f >= amps) return f;
  return kFuses[kFuseCount - 1];
}

const Cable* cableFor(int fuse, double pW, double lM, double limit, bool three, double k = 56) {
  for (const auto& c : kCables) {
    if (c.ampacity < fuse) continue;
    double e = three ? voltageDrop3ph(pW, lM, c.section, k) : voltageDrop1ph(pW, lM, c.section, k);
    if (e <= limit) return &c;
  }
  return nullptr;
}

void putCable(Out& o, const std::string& p, const Cable* c) {
  if (!c) {
    o.put(p + ".name", "");
    return;
  }
  o.put(p + ".name", c->name);
  o.put(p + ".section", c->section);
  o.put(p + ".ampacity", c->ampacity);
  o.put(p + ".toprak", c->toprak);
  o.put(p + ".hava", c->hava);
  o.put(p + ".r", c->r);
  o.put(p + ".x", c->x);
}

// ───────────────────────── modüller ─────────────────────────

/// Aydınlatma (verim metodu): Φ = E·A·d / η ; n = Φ / Φarmatür ; ızgara yerleşimi
void lighting(const In& in, Out& o) {
  double a = in.d("a", 6), b = in.d("b", 4), H = in.d("H", 3), work = in.d("work", 0.85), susp = in.d("susp", 0);
  double lux = in.d("lux", 300), soil = in.d("soiling", 1.25), phi = in.d("flux", 3600);
  int combo = in.i("combo", 0);
  double h = H - work - susp;
  double k = roomIndex(a, b, h);
  double eta = utilizationFor(k, combo);
  double area = a * b;
  double total = eta == 0 ? 0 : lux * area * soil / eta;
  double exact = phi == 0 ? 0 : total / phi;
  int n = std::max(1, static_cast<int>(std::ceil(exact)));
  int bestC = n, bestR = 1;
  double bestScore = INFINITY;
  for (int c = 1; c <= n; c++) {
    int r = static_cast<int>(std::ceil(static_cast<double>(n) / c));
    int waste = c * r - n;
    double ratio = (a / c) / (b / r);
    double score = std::fabs(std::log(ratio)) + waste * 0.35;
    if (score < bestScore) {
      bestScore = score;
      bestC = c;
      bestR = r;
    }
  }
  int placed = bestC * bestR;
  double achieved = area == 0 ? 0 : placed * phi * eta / (area * soil);
  o.put("h", h);
  o.put("k", k);
  o.put("eta", eta);
  o.put("area", area);
  o.put("total_flux", total);
  o.put("exact_count", exact);
  o.put("count", placed);
  o.put("cols", bestC);
  o.put("rows", bestR);
  o.put("achieved_lux", achieved);
}

/// Gerilim düşümü + kesit önerisi + ısınma kontrolü
void voltageDrop(const In& in, Out& o) {
  bool three = in.b("three", true);
  double k = in.d("k", 56), p = in.d("p", 11008), l = in.d("l", 30), s = in.d("s", 10), limit = in.d("limit", 1.5),
         cosPhi = in.d("cos", 0.8);
  double u = three ? 380.0 : 220.0;
  double e = three ? voltageDrop3ph(p, l, s, k) : voltageDrop1ph(p, l, s, k);
  double i = three ? current3ph(p, 380, cosPhi) : current1ph(p, 220, cosPhi);
  int fuse = nextFuse(i);
  double sMin = three ? 100 * l * p / (k * limit * u * u) : 200 * l * p / (k * limit * u * u);
  const Cable* suggested = &kCables[kCableCount - 1];
  for (const auto& c : kCables)
    if (c.section >= sMin && c.ampacity >= fuse) {
      suggested = &c;
      break;
    }
  const Cable* selected = nullptr;
  for (const auto& c : kCables)
    if (std::fabs(c.section - s) < 0.01) selected = &c;
  o.put("u", u);
  o.put("e", e);
  o.put("dv", u * e / 100);
  o.put("i", i);
  o.put("fuse", fuse);
  o.put("s_min", sMin);
  o.put("drop_ok", e <= limit);
  putCable(o, "suggested", suggested);
  putCable(o, "selected", selected);
  o.put("heat_ok", selected ? selected->ampacity >= fuse : false);
}

/// Eşzamanlılık: daire %60/%40 + bina katsayısı + ortak alan
void diversity(const In& in, Out& o) {
  double flatW = in.d("flat_w", 16000), common = in.d("common", 6000), commonFactor = in.d("common_factor", 1.0),
         cosPhi = in.d("cos", 0.8);
  int flats = in.i("flats", 12);
  double perFlat = dwellingDemand(flatW);
  int f = buildingFactor(flats);
  double flatsDemand = flats * perFlat * f / 100;
  double commonDemand = common * commonFactor;
  double total = flatsDemand + commonDemand;
  double i = current3ph(total, 380, cosPhi);
  int row = -1;
  if (flats >= 3)
    for (int r = 0; r < kDiversityCount; r++)
      if (flats >= kBuildingDiversity[r][0] && flats <= kBuildingDiversity[r][1]) row = r;
  o.put("per_flat", perFlat);
  o.put("factor", f);
  o.put("flats_demand", flatsDemand);
  o.put("common_demand", commonDemand);
  o.put("total", total);
  o.put("i", i);
  o.put("fuse", nextFuse(i));
  o.put("row", row);
}

/// Topraklama: şerit, çubuk, temel, gözlü; paralel eşdeğer; Wenner; Onderdonk
void grounding(const In& in, Out& o) {
  double rho = in.d("rho", 100);
  auto strip = [&] {
    double L = in.d("strip_l", 106), d = in.d("strip_d", 0.008), H = in.d("strip_h", 1);
    return rho / (kPi * L) * (std::log(2 * L / d) + std::log(L / (2 * H)));
  };
  auto rod = [&] {
    double L = in.d("rod_l", 1.75), d = in.d("rod_d", 0.02);
    int n = std::max(1, in.i("rod_n", 8));
    return rho / (2 * kPi * L) * std::log(4 * L / d) / n;
  };
  double fa = in.d("fa", 17), fb = in.d("fb", 28);
  double rStrip = in.b("use_strip", true) ? strip() : 0;
  double rRod = in.b("use_rod", true) ? rod() : 0;
  double rFound = in.b("use_found", true) ? 2 * rho / (kPi * 1.13 * std::sqrt(fa * fb)) : 0;
  double rMesh = in.b("use_mesh", false) ? rho * (1 / (4 * std::sqrt(fa * fb / kPi)) + 1 / in.d("mesh_l", 170)) : 0;
  double inv = 0;
  for (double r : {rStrip, rRod, rFound, rMesh})
    if (r > 0 && std::isfinite(r)) inv += 1 / r;
  double rt = inv > 0 ? 1 / inv : 0;
  double ia = in.d("ia", 25), ul = in.d("ul", 50);
  double rMax = ia == 0 ? 0 : ul / ia;
  double area = fa * fb;
  int minRods = area <= 100 ? 2 : area <= 1000 ? 4 : area <= 2000 ? 6 : 8;
  // Wenner: ρ = 2π·a·R ; ortalama × mevsim katsayısı (eski uygulamadaki gibi)
  double sum = 0;
  int measured = 0;
  static const double kWennerA[4] = {1, 2, 4, 6};
  for (int i = 0; i < 4; i++) {
    double a = in.d("w_a." + std::to_string(i), kWennerA[i]);
    double r = in.d("w_r." + std::to_string(i), 0);
    double w = 2 * kPi * a * r;
    o.put("w_rho." + std::to_string(i), w);
    sum += w;
    if (r > 0) measured++;
  }
  double season = in.d("season", 2);
  double rhoAvg = measured == 0 ? 0 : season / 4 * sum * 4 / measured;
  double ika = in.d("ika", 10), t = in.d("t", 1);
  o.put("r_strip", rStrip);
  o.put("r_rod", rRod);
  o.put("r_found", rFound);
  o.put("r_mesh", rMesh);
  o.put("r_total", rt);
  o.put("r_max", rMax);
  o.put("ok", rt > 0 && rt <= rMax);
  o.put("min_rods", minRods);
  o.put("rho_avg", rhoAvg);
  o.put("onderdonk_cadweld", ika * 7.06 * std::sqrt(t) / 2);
  o.put("onderdonk_lehim", ika * 9.24 * std::sqrt(t) / 2);
  o.put("onderdonk_civata", ika * 11.7 * std::sqrt(t) / 2);
}

/// Paratoner: risk analizi (Nd/Nc), koruma seviyesi, ESE koruma yarıçapı
void lightning(const In& in, Out& o) {
  double l = in.d("l", 26), w = in.d("w", 21), h = in.d("h", 30), nk = in.d("nk", 20);
  double c1 = in.d("c1", 0.25), c2 = in.d("c2", 1), c3 = in.d("c3", 0.5), c4 = in.d("c4", 1), c5 = in.d("c5", 5);
  double ae = l * w + 6 * h * (l + w) + 9 * kPi * h * h;
  double ng = 0.04 * std::pow(nk, 1.25);
  double nd = ng * ae * c1 * 1e-6;
  double c = c2 * c3 * c4 * c5;
  double nc = 5.5e-3 / c;
  double e = 0;
  int level = 0;
  if (nd > nc) {
    e = 1 - nc / nd;
    level = e > 0.95 ? 1 : e > 0.90 ? 2 : e > 0.80 ? 3 : 4;
  }
  int over = in.i("level", 0);
  int used = over > 0 ? over : (level == 0 ? 3 : level);
  double d = lpLevelD(used);
  double mast = in.d("mast", 6), dt = in.d("delta_t", 60);
  double rp = std::sqrt(mast * (2 * d - mast) + dt * (2 * d + dt));
  double halfDiag = std::sqrt(l * l + w * w) / 2;
  o.put("ae", ae);
  o.put("ng", ng);
  o.put("nd", nd);
  o.put("c", c);
  o.put("nc", nc);
  o.put("e", e);
  o.put("level", level);
  o.put("level_used", used);
  o.put("d", d);
  o.put("rp", rp);
  o.put("half_diag", halfDiag);
  o.put("covers", rp >= halfDiag);
}

/// Motor: Tablo 1.1 satırı + hesaplanan akım
void motor(const In& in, Out& o) {
  double kw = in.d("kw", 7.5), cosPhi = in.d("cos", 0.85), eff = in.d("eff", 0.88);
  int idx = kMotorCount - 1;
  for (int i = 0; i < kMotorCount; i++)
    if (kMotor[i].kw >= kw - 1e-9) {
      idx = i;
      break;
    }
  const auto& r = kMotor[idx];
  o.put("row", idx);
  o.put("kw", r.kw);
  o.put("hp", r.hp);
  o.put("amps", r.amps);
  o.put("kesit", r.kesit);
  o.put("termik", r.termik);
  o.put("buson_normal", r.busonNormal);
  o.put("buson_gecikmeli", r.busonGecikmeli);
  o.put("nh", r.nh);
  o.put("i_calc", kw * 1000 / (std::sqrt(3.0) * 380 * cosPhi * eff));
}

/// Kablo seçimi: sigortaya göre (hesap / toprak / hava × düzeltme katsayısı)
void cable(const In& in, Out& o) {
  double amps = in.d("amps", 45), factor = in.d("factor", 1.0);
  int mode = in.i("mode", 0);
  int fuse = nextFuse(amps);
  int idx = -1;
  for (int i = 0; i < kCableCount; i++) {
    double cap = (mode == 1 ? kCables[i].toprak : mode == 2 ? kCables[i].hava : kCables[i].ampacity) * factor;
    if (cap >= fuse) {
      idx = i;
      break;
    }
  }
  o.put("fuse", fuse);
  o.put("index", idx);
  putCable(o, "cable", idx >= 0 ? &kCables[idx] : nullptr);
}

/// Sayaç panosu: gereken sayaç adedini karşılayan ilk satır, her kat seçeneği için
void meterPanel(const In& in, Out& o) {
  int need = in.i("need", 14);
  bool three = in.b("three", false);
  int floors3[] = {2, 3}, floors1[] = {2, 3, 4};
  double hs3[] = {150, 200}, hs1[] = {130, 165, 200};
  int nf = three ? 2 : 3;
  int n = 0;
  for (int fi = 0; fi < nf; fi++) {
    for (const auto& r : kMeterPanel) {
      int total = static_cast<int>(three ? r[7 + fi] : r[1 + fi]);
      if (total >= need) {
        std::string p = "option." + std::to_string(n) + ".";
        double h = three ? hs3[fi] : hs1[fi];
        o.put(p + "per_row", static_cast<int>(r[0]));
        o.put(p + "floors", three ? floors3[fi] : floors1[fi]);
        o.put(p + "total", total);
        o.put(p + "width", three ? r[9] : r[4]);
        o.put(p + "room_a", three ? r[10] : r[5]);
        o.put(p + "room_b", three ? r[11] : r[6]);
        o.put(p + "h", h);
        o.put(p + "room_h", h + 50);
        n++;
        break;
      }
    }
  }
  o.put("options", n);
}

/// Yükleme cetveli: linyeler, sigorta/kesit, gerilim düşümü, faz dengesi, kolon
void loadSchedule(const In& in, Out& o) {
  bool board3 = in.b("three", true);
  int demandMode = in.i("demand_mode", 0);
  double demandFactor = in.d("demand_factor", 0.6), kolonLen = in.d("kolon_l", 15), limit = in.d("limit", 1.5),
         cosPhi = in.d("cos", 0.8);
  struct L {
    std::string name, type, phase;
    int count;
    double unitW, length;
    double power() const { return count * unitW; }
    bool three() const { return phase == "RST"; }
  };
  std::vector<L> ls;
  int n = in.i("n", 0);
  for (int i = 0; i < n; i++) {
    std::string p = "linye." + std::to_string(i) + ".";
    ls.push_back({in.s(p + "name", "L" + std::to_string(i + 1)), in.s(p + "type", "L"), in.s(p + "phase", "R"),
                  in.i(p + "count", 1), in.d(p + "unit_w", 100), in.d(p + "length", 15)});
  }
  // faz dengele: üç fazlılar eşit, tek fazlılar büyükten küçüğe en az yüklü faza
  if (in.b("balance", false)) {
    double load[3] = {0, 0, 0};
    for (auto& l : ls)
      if (l.three())
        for (double& x : load) x += l.power() / 3;
    std::vector<L*> singles;
    for (auto& l : ls)
      if (!l.three()) singles.push_back(&l);
    std::stable_sort(singles.begin(), singles.end(), [](L* a, L* b) { return a->power() > b->power(); });
    for (L* l : singles) {
      int m = 0;
      for (int k = 1; k < 3; k++)
        if (load[k] < load[m]) m = k;
      l->phase = std::string(1, "RST"[m]);
      load[m] += l->power();
    }
  }
  double installed = 0, phase[3] = {0, 0, 0}, worstDrop = -1;
  int worst = -1;
  for (size_t i = 0; i < ls.size(); i++) {
    const L& l = ls[i];
    double power = l.power();
    double cur = l.three() ? current3ph(power, 380, 0.85) : current1ph(power);
    int base = l.type == "L" ? 10 : 16;
    int fuse = std::max(base, nextFuse(cur * 1.15));
    double sec = (l.type == "L" && fuse <= 10) ? 1.5 : fuse <= 16 ? 2.5 : fuse <= 25 ? 4 : fuse <= 32 ? 6 : 10;
    double drop = l.three() ? voltageDrop3ph(power, l.length, sec) : voltageDrop1ph(power, l.length, sec);
    std::string p = "linye." + std::to_string(i) + ".";
    o.put(p + "name", l.name);
    o.put(p + "phase", l.phase);
    o.put(p + "power", power);
    o.put(p + "current", cur);
    o.put(p + "fuse", fuse);
    o.put(p + "section", sec);
    o.put(p + "cable", std::string(l.three() ? "5" : "3") + "x" + fmt(sec, std::fmod(sec, 1.0) == 0 ? 0 : 1) + " NYM");
    o.put(p + "drop", drop);
    installed += power;
    if (l.three())
      for (double& x : phase) x += power / 3;
    else
      phase[l.phase == "S" ? 1 : l.phase == "T" ? 2 : 0] += power;
    if (worst < 0 || drop > worstDrop) {  // eşitlikte ilk linye (eski uygulamadaki reduce gibi)
      worstDrop = drop;
      worst = static_cast<int>(i);
    }
  }
  double demand = demandMode == 0 ? dwellingDemand(installed) : installed * demandFactor;
  double iB = board3 ? current3ph(demand, 380, cosPhi) : current1ph(demand, 220, cosPhi);
  int mainFuse = nextFuse(iB);
  const Cable* kolon = cableFor(mainFuse, demand, kolonLen, limit, board3);
  double kolonDrop = kolon ? (board3 ? voltageDrop3ph(demand, kolonLen, kolon->section) : voltageDrop1ph(demand, kolonLen, kolon->section))
                           : NAN;
  double maxP = std::max({phase[0], phase[1], phase[2]}), minP = std::min({phase[0], phase[1], phase[2]});
  o.put("n", static_cast<int>(ls.size()));
  o.put("installed", installed);
  o.put("demand", demand);
  o.put("board_current", iB);
  o.put("main_fuse", mainFuse);
  putCable(o, "kolon", kolon);
  o.put("kolon_drop", kolonDrop);
  o.put("worst", worst);
  o.put("total_drop", kolonDrop + (worst >= 0 ? worstDrop : 0));
  o.put("phase_r", phase[0]);
  o.put("phase_s", phase[1]);
  o.put("phase_t", phase[2]);
  o.put("unbalance", maxP == 0 ? 0.0 : (maxP - minP) / maxP * 100);
  o.put("kakr", std::string("30mA ") + (board3 ? "4x" : "2x") + (mainFuse <= 40 ? "40" : "63") + "A K.A.K.R.");
}

// ───────────────────────── tablolar ─────────────────────────

std::string g(double v) {
  char b[48];
  std::snprintf(b, sizeof(b), "%.10g", v);
  return b;
}

std::string named(const Named* t, int n) {
  std::string s = "name\tvalue\n";
  for (int i = 0; i < n; i++) s += std::string(t[i].name) + "\t" + g(t[i].value) + "\n";
  return s;
}

std::string table(const std::string& name) {
  std::string s;
  if (name == "cables") {
    s = "name\tsection\tampacity\ttoprak\thava\tr\tx\n";
    for (const auto& c : kCables)
      s += std::string(c.name) + "\t" + g(c.section) + "\t" + g(c.ampacity) + "\t" + g(c.toprak) + "\t" + g(c.hava) + "\t" + g(c.r) + "\t" + g(c.x) + "\n";
  } else if (name == "nyy") {
    s = "name\tr\ttoprak\thava\tkg\n";
    for (const auto& c : kNyy) s += std::string(c.name) + "\t" + g(c.r) + "\t" + g(c.toprak) + "\t" + g(c.hava) + "\t" + g(c.kg) + "\n";
  } else if (name == "fuses") {
    s = "amps\n";
    for (int f : kFuses) s += std::to_string(f) + "\n";
  } else if (name == "motor") {
    s = "kw\thp\tamps\tkesit\ttermik\tbuson_normal\tbuson_gecikmeli\tnh\n";
    for (const auto& m : kMotor)
      s += g(m.kw) + "\t" + m.hp + "\t" + g(m.amps) + "\t" + g(m.kesit) + "\t" + m.termik + "\t" + m.busonNormal + "\t" + m.busonGecikmeli + "\t" + m.nh + "\n";
  } else if (name == "diversity") {
    s = "min\tmax\tfactor\n";
    for (const auto& r : kBuildingDiversity) s += std::to_string(r[0]) + "\t" + std::to_string(r[1]) + "\t" + std::to_string(r[2]) + "\n";
  } else if (name == "combos") {
    s = "name\n";
    for (const char* c : kCombos) s += std::string(c) + "\n";
  } else if (name == "utilization") {
    s = "k";
    for (const char* c : kCombos) s += std::string("\t") + c;
    s += "\n";
    for (int i = 0; i < kKCount; i++) {
      s += g(kRoomIndexK[i]);
      for (int j = 0; j < kComboCount; j++) s += "\t" + g(kUtilization[i][j]);
      s += "\n";
    }
  } else if (name == "soiling") {
    s = named(kSoiling, 4);
  } else if (name == "luminaires") {
    s = named(kLuminaires, 12);
  } else if (name == "conductors") {
    s = named(kConductors, 11);
  } else if (name == "lp_c1") {
    s = named(kLpC1, 4);
  } else if (name == "lp_c2") {
    s = named(kLpC2, 5);
  } else if (name == "lp_c3") {
    s = named(kLpC3, 4);
  } else if (name == "lp_c4") {
    s = named(kLpC4, 3);
  } else if (name == "lp_c5") {
    s = named(kLpC5, 3);
  } else if (name == "meter_panel") {
    s = "per_row\tt1_2\tt1_3\tt1_4\tw1\ta1\tb1\tt3_2\tt3_3\tw3\ta3\tb3\n";
    for (const auto& r : kMeterPanel) {
      for (int i = 0; i < 12; i++) s += (i ? "\t" : "") + g(r[i]);
      s += "\n";
    }
  } else if (name == "lux") {
    s = "category\tname\tgenel\tozel\n";
    for (int i = 0; i < kLuxCount; i++)
      s += std::string(kLux[i].category) + "\t" + kLux[i].name + "\t" + (kLux[i].genel < 0 ? "" : g(kLux[i].genel)) + "\t" +
           (kLux[i].ozel < 0 ? "" : g(kLux[i].ozel)) + "\n";
  } else if (name == "prices") {
    s = "group\tname\tref\tprice\n";
    for (int i = 0; i < kPriceCount; i++)
      s += std::string(kPrices[i].group) + "\t" + kPrices[i].name + "\t" + kPrices[i].ref + "\t" + g(kPrices[i].price) + "\n";
  }
  return s;
}

thread_local std::string g_out;

}  // namespace
}  // namespace mc

extern "C" {

MC_API const char* mc_calc(const char* module, const char* input) {
  using namespace mc;
  In in(input);
  Out o;
  std::string m = module ? module : "";
  try {
    if (m == "lighting") lighting(in, o);
    else if (m == "voltage_drop") voltageDrop(in, o);
    else if (m == "diversity") diversity(in, o);
    else if (m == "grounding") grounding(in, o);
    else if (m == "lightning") lightning(in, o);
    else if (m == "motor") motor(in, o);
    else if (m == "cable") cable(in, o);
    else if (m == "meter_panel") meterPanel(in, o);
    else if (m == "load_schedule") loadSchedule(in, o);
    else o.put("error", "Bilinmeyen modül: " + m);
  } catch (const std::exception& e) {
    o.put("error", e.what());
  }
  g_out = o.str();
  return g_out.c_str();
}

MC_API const char* mc_table(const char* name) {
  mc::g_out = mc::table(name ? name : "");
  return mc::g_out.c_str();
}

}  // extern "C"
