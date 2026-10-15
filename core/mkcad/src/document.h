// mkcad belge modeli: ham DXF etiketleri korunur (kayıpsız geri yazım için),
// sahne üretimi bu etiketleri yorumlar.
#pragma once

#include <cstdint>
#include <map>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

#include "mkcad.h"

namespace mk {

struct Tag {
  int code;
  std::string value;  // UTF-8
  double d() const;
  int i() const;
};

struct Entity {
  std::string type;
  std::vector<Tag> tags;
  std::vector<Entity> children;  // POLYLINE→VERTEX, INSERT→ATTRIB
  std::vector<Tag> seqend;       // POLYLINE/INSERT kapanışı (kayıpsız yazım için)
  bool deleted = false;

  const Tag* find(int code) const;
  Tag* findMut(int code);
  double f(int code, double def = 0) const;
  int n(int code, int def = 0) const;
  std::string s(int code, const std::string& def = "") const;
  std::string layer() const { return s(8, "0"); }
  int color() const { return n(62, 256); }  // 256 BYLAYER, 0 BYBLOCK
  bool flipped() const { return f(230, 1) < 0; }  // OCS Z = -1
};

struct Layer {
  std::string name;
  int aci = 7;
  bool off = false;
  bool frozen = false;
  int32_t count = 0;  // sahnedeki ilkel sayısı
};

struct Block {
  std::string name;
  double bx = 0, by = 0;
  std::vector<Entity> entities;
};

/// Orijinal dosyanın bir bölümü (ENTITIES dışındakiler aynen geri yazılır).
struct Section {
  std::string name;
  std::vector<Tag> tags;  // "0 SECTION" ve "0 ENDSEC" hariç gövde
};

/// Geri alma kaydı: dokunulan nesnelerin önceki ve sonraki hali.
struct Change {
  int idx;
  Entity before;
  Entity after;
};
struct Operation {
  std::vector<Change> changes;
};

struct Document {
  std::string version;
  std::string codepage;
  std::vector<Layer> layers;
  std::unordered_map<std::string, int> layerIndex;
  std::unordered_map<std::string, Block> blocks;
  std::vector<Entity> entities;
  std::string typeStats;
  std::vector<Section> sections;  // dosyadaki sırasıyla; ENTITIES yer tutucu
  uint64_t maxHandle = 0;
  uint64_t loadedMaxHandle = 0;  // yeni handle verilmediyse $HANDSEED aynen kalır
  std::string modelOwner;  // model alanı BLOCK_RECORD handle'ı (330)
  bool dirty = false;

  // geri al / yinele
  std::vector<Operation> undo, redo;
  Operation pending;
  bool inOp = false;

  // sahne tamponları
  std::vector<float> vertices;
  std::vector<int32_t> segOwner;  // her çizgi parçasının üst düzey nesne indeksi
  std::vector<MkBatch> batches;
  std::vector<MkPoly> polys;
  std::vector<int32_t> polyOwner;
  std::vector<MkText> texts;
  std::vector<int32_t> textOwner;
  std::string textPool;
  float ext[4] = {0, 0, 0, 0};

  // tek nesne geometrisi (seçim vurgusu)
  std::vector<float> scratch;
  std::string scratchStr;

  int layerOf(const std::string& name);  // yoksa oluşturur
  int versionNumber() const;             // "AC1015" → 1015
  std::string newHandle();
};

// reader.cpp
std::unique_ptr<Document> readDxf(const uint8_t* data, size_t len, std::string& error);

// writer.cpp
std::string writeDxf(Document& doc);

// text.cpp
std::string cleanText(const std::string& s);
std::string cleanMText(const std::string& s);

// scene.cpp
void buildScene(Document& doc);
/// Tek bir üst düzey nesnenin dünya geometrisini (x1,y1,x2,y2 dörtlüleri) üretir.
void entityGeometry(Document& doc, int idx, std::vector<float>& out);

// edit.cpp
int pickEntity(Document& doc, double x, double y, double tol);
int snapPoint(Document& doc, double x, double y, double tol, double& ox, double& oy);
void beginOp(Document& doc);
void touch(Document& doc, int idx);
void commitOp(Document& doc);
bool translateEntity(Entity& e, double dx, double dy);
/// x' = a·x + c·y + e ; y' = b·x + d·y + f
struct Xf {
  double a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;
  double rot = 0;      // derece (döndürme)
  double s = 1;        // ölçek
  bool mir = false;    // ayna
  double phi = 0;      // ayna doğrusunun açısı (derece)

  void pt(double& x, double& y) const {
    double nx = a * x + c * y + e, ny = b * x + d * y + f;
    x = nx;
    y = ny;
  }
  void vec(double& x, double& y) const {
    double nx = a * x + c * y, ny = b * x + d * y;
    x = nx;
    y = ny;
  }
  /// Doğrultu açısı (derece): döndürmede eklenir, aynada 2φ − a
  double angle(double deg) const { return mir ? 2 * phi - deg : deg + rot; }

  /// OCS Z = −1 düzlemi için X aynasıyla eşlenik: F·M·F
  Xf conj() const {
    Xf o = *this;
    o.b = -b;
    o.c = -c;
    o.e = -e;
    o.rot = -rot;
    o.phi = -phi;
    return o;
  }
};

Xf makeRotate(double cx, double cy, double deg);
Xf makeScale(double cx, double cy, double k);
Xf makeMirror(double x1, double y1, double x2, double y2);
bool transformEntity(Entity& e, const Xf& X);
bool defineBlock(Document& doc, const std::string& name, const std::string& snippet, std::string& error);
std::string insertCounts(const Document& doc);
std::string groupName(Document& doc);
bool makeGroupBlock(Document& doc, const std::vector<int>& ids, const std::string& name, double bx, double by,
                    std::string& error);
bool recolorInsert(Document& doc, Entity& ins, int aci, std::string& error);
bool explodeInsert(Document& doc, const Entity& ins, std::vector<Entity>& out, std::string& error);
bool offsetEntity(const Entity& src, double d, double px, double py, Entity& out);
std::string entityText(const Entity& e);
bool setEntityText(Entity& e, const std::string& utf8);
void setEntityLayer(Entity& e, const std::string& layer);
void setEntityColor(Entity& e, int aci);
int selectRect(Document& doc, double x0, double y0, double x1, double y1, bool crossing, std::vector<int32_t>& out);
bool undoOp(Document& doc);
bool redoOp(Document& doc);

}  // namespace mk
