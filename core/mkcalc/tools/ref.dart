// Referans: ESKİ uygulamanın formülleri (lib/calc/formulas.dart) + modül ekranlarındaki hesap
// satırları (aynen kopya). C++ mkcalc çıktısıyla karşılaştırmak için kullanılır.
// Kullanım: dart run ref.dart cases.txt > ref_out.txt
import 'dart:io';
import 'dart:math' as math;

import '../../../../elektrik_cizim/lib/calc/formulas.dart';
import '../../../../elektrik_cizim/lib/data/tables.dart';

Map<String, String> parse(String block) {
  final m = <String, String>{};
  for (final line in block.split('\n')) {
    final i = line.indexOf('=');
    if (i > 0) m[line.substring(0, i)] = line.substring(i + 1);
  }
  return m;
}

double d(Map<String, String> m, String k, double def) => double.tryParse(m[k] ?? '') ?? def;
int n(Map<String, String> m, String k, int def) => (double.tryParse(m[k] ?? '') ?? def).round();
bool b(Map<String, String> m, String k, bool def) => m.containsKey(k) ? m[k] == '1' : def;

void main(List<String> args) {
  final blocks = File(args[0]).readAsStringSync().split('---\n');
  final out = StringBuffer();
  for (final blk in blocks) {
    if (blk.trim().isEmpty) continue;
    final m = parse(blk);
    final mod = m['module']!;
    final o = <String, Object>{};
    switch (mod) {
      case 'lighting':
        final r = lighting(
          a: d(m, 'a', 6), b: d(m, 'b', 4), roomHeight: d(m, 'H', 3), workPlane: d(m, 'work', 0.85),
          suspension: d(m, 'susp', 0), lux: d(m, 'lux', 300), combo: n(m, 'combo', 0), soiling: d(m, 'soiling', 1.25),
          lumPerFixture: d(m, 'flux', 3600));
        o.addAll({'h': r.h, 'k': r.k, 'eta': r.eta, 'area': r.area, 'total_flux': r.totalFlux, 'exact_count': r.exactCount,
          'count': r.count, 'cols': r.cols, 'rows': r.rows, 'achieved_lux': r.achievedLux});
      case 'voltage_drop':
        final three = b(m, 'three', true);
        final k = d(m, 'k', 56), p = d(m, 'p', 11008), l = d(m, 'l', 30), s = d(m, 's', 10), limit = d(m, 'limit', 1.5), cosPhi = d(m, 'cos', 0.8);
        final u = three ? 380.0 : 220.0;
        final e = three ? voltageDrop3ph(p, l, s, k: k) : voltageDrop1ph(p, l, s, k: k);
        final i = three ? current3ph(p, cos: cosPhi) : current1ph(p, cos: cosPhi);
        final fuse = nextFuse(i);
        final sMin = three ? 100 * l * p / (k * limit * u * u) : 200 * l * p / (k * limit * u * u);
        final suggested = cables.firstWhere((c) => c.section >= sMin && c.ampacity >= fuse, orElse: () => cables.last);
        o.addAll({'e': e, 'dv': u * e / 100, 'i': i, 'fuse': fuse, 's_min': sMin, 'suggested.name': suggested.name});
      case 'diversity':
        final flatW = d(m, 'flat_w', 16000), common = d(m, 'common', 6000), cf = d(m, 'common_factor', 1), cosPhi = d(m, 'cos', 0.8);
        final flats = n(m, 'flats', 12);
        final perFlat = dwellingDemand(flatW);
        final f = buildingFactor(flats);
        final flatsDemand = flats * perFlat * f / 100;
        final total = flatsDemand + common * cf;
        final i = current3ph(total, cos: cosPhi);
        o.addAll({'per_flat': perFlat, 'factor': f, 'flats_demand': flatsDemand, 'total': total, 'i': i, 'fuse': nextFuse(i)});
      case 'grounding':
        final rho = d(m, 'rho', 100);
        final fa = d(m, 'fa', 17), fb = d(m, 'fb', 28);
        final rStrip = b(m, 'use_strip', true) ? groundStrip(rho, d(m, 'strip_l', 106), d(m, 'strip_d', 0.008), d(m, 'strip_h', 1)) : 0.0;
        final rRod = b(m, 'use_rod', true) ? groundRod(rho, d(m, 'rod_l', 1.75), d(m, 'rod_d', 0.02), n(m, 'rod_n', 8)) : 0.0;
        final rFound = b(m, 'use_found', true) ? groundFoundation(rho, fa * fb) : 0.0;
        final rMesh = b(m, 'use_mesh', false) ? groundMesh(rho, fa, fb, d(m, 'mesh_l', 170)) : 0.0;
        final rt = parallel([rStrip, rRod, rFound, rMesh]);
        final ika = d(m, 'ika', 10), t = d(m, 't', 1);
        final wa = [1.0, 2.0, 4.0, 6.0];
        final wr = [for (var i = 0; i < 4; i++) d(m, 'w_r.$i', 0)];
        final wRho = [for (var i = 0; i < 4; i++) wenner(wa[i], wr[i])];
        final measured = wr.where((r) => r > 0).length;
        final season = d(m, 'season', 2);
        final rhoAvg = measured == 0 ? 0.0 : season / 4 * wRho.fold<double>(0, (s, v) => s + v) * 4 / measured;
        o.addAll({'r_strip': rStrip, 'r_rod': rRod, 'r_found': rFound, 'r_mesh': rMesh, 'r_total': rt, 'rho_avg': rhoAvg,
          'onderdonk_cadweld': onderdonk(ika, t, 7.06), 'onderdonk_lehim': onderdonk(ika, t, 9.24), 'onderdonk_civata': onderdonk(ika, t, 11.7)});
      case 'lightning':
        final l = d(m, 'l', 26), w = d(m, 'w', 21);
        final r = lightningRisk(l: l, w: w, h: d(m, 'h', 30), nk: d(m, 'nk', 20), c1: d(m, 'c1', 0.25), c2: d(m, 'c2', 1),
            c3: d(m, 'c3', 0.5), c4: d(m, 'c4', 1), c5: d(m, 'c5', 5));
        final level = n(m, 'level', 0) > 0 ? n(m, 'level', 0) : (r.level == 0 ? 3 : r.level);
        final rp = eseRadius(d(m, 'mast', 6), lpLevelD[level]!, d(m, 'delta_t', 60));
        o.addAll({'ae': r.ae, 'ng': r.ng, 'nd': r.nd, 'c': r.c, 'nc': r.nc, 'e': r.e, 'level': r.level, 'level_used': level, 'rp': rp});
      case 'motor':
        final kw = d(m, 'kw', 7.5);
        final row = motorRowFor(kw);
        o.addAll({'row': motorTable.indexOf(row), 'amps': row.amps, 'kesit': row.kesit, 'termik': row.termik, 'nh': row.nh,
          'i_calc': motorCurrent(kw, cos: d(m, 'cos', 0.85), eff: d(m, 'eff', 0.88))});
      case 'cable':
        final amps = d(m, 'amps', 45), factor = d(m, 'factor', 1);
        final mode = n(m, 'mode', 0);
        final fuse = nextFuse(amps);
        var idx = -1;
        for (var i = 0; i < cables.length; i++) {
          final cap = switch (mode) { 1 => cables[i].toprak, 2 => cables[i].hava, _ => cables[i].ampacity } * factor;
          if (cap >= fuse) { idx = i; break; }
        }
        o.addAll({'fuse': fuse, 'index': idx});
      case 'load_schedule':
        // lib/screens/load_schedule_screen.dart Linye + build() hesapları
        final board3 = b(m, 'three', true);
        final demandMode = n(m, 'demand_mode', 0);
        final demandFactor = d(m, 'demand_factor', 0.6), kolonLen = d(m, 'kolon_l', 15), limit = d(m, 'limit', 1.5), cosPhi = d(m, 'cos', 0.8);
        final cnt = n(m, 'n', 0);
        final ls = [
          for (var i = 0; i < cnt; i++)
            (type: m['linye.$i.type'] ?? 'L', count: n(m, 'linye.$i.count', 1), unitW: d(m, 'linye.$i.unit_w', 100),
              length: d(m, 'linye.$i.length', 15), phase: m['linye.$i.phase'] ?? 'R')
        ];
        double power(dynamic l) => l.count * l.unitW;
        var installed = 0.0;
        final ph = {'R': 0.0, 'S': 0.0, 'T': 0.0};
        double worstDrop = -1;
        var worst = -1;
        for (var i = 0; i < ls.length; i++) {
          final l = ls[i];
          final three = l.phase == 'RST';
          final p = power(l);
          final cur = three ? current3ph(p, cos: 0.85) : current1ph(p);
          final base = l.type == 'L' ? 10 : 16;
          final fuse = math.max(base, nextFuse(cur * 1.15));
          final sec = (l.type == 'L' && fuse <= 10) ? 1.5 : fuse <= 16 ? 2.5 : fuse <= 25 ? 4.0 : fuse <= 32 ? 6.0 : 10.0;
          final drop = three ? voltageDrop3ph(p, l.length, sec) : voltageDrop1ph(p, l.length, sec);
          o['linye.$i.fuse'] = fuse;
          o['linye.$i.section'] = sec;
          o['linye.$i.drop'] = drop;
          o['linye.$i.current'] = cur;
          installed += p;
          if (three) { for (final k in ph.keys) { ph[k] = ph[k]! + p / 3; } } else { ph[l.phase] = ph[l.phase]! + p; }
          if (worst < 0 || drop > worstDrop) { worstDrop = drop; worst = i; }
        }
        final demand = demandMode == 0 ? dwellingDemand(installed) : installed * demandFactor;
        final iB = board3 ? current3ph(demand, cos: cosPhi) : current1ph(demand, cos: cosPhi);
        final mainFuse = nextFuse(iB);
        final kolon = board3
            ? cableFor(fuse: mainFuse, pW: demand, lM: kolonLen, limit: limit)
            : cables.cast<dynamic>().firstWhere((c) => c.ampacity >= mainFuse && voltageDrop1ph(demand, kolonLen, c.section) <= limit, orElse: () => null);
        o.addAll({'installed': installed, 'demand': demand, 'board_current': iB, 'main_fuse': mainFuse,
          'kolon.name': kolon?.name ?? '', 'phase_r': ph['R']!, 'phase_s': ph['S']!, 'phase_t': ph['T']!});
    }
    out.writeln('case=${m['case']}');
    o.forEach((k, v) => out.writeln('$k=$v'));
    out.write('---\n');
  }
  stdout.write(out);
}
