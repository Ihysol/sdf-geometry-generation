# Konzept: DAG-basierte Datenstruktur für einen Dual-Contouring-Voxelrenderer

## 1. Ziel

Der Renderer soll große, hochauflösende Voxel- bzw. volumetrische Welten
effizient speichern, verarbeiten und darstellen. Als zentrale
Runtime-Datenstruktur wird ein **Directed Acyclic Graph (DAG)**
verwendet, der aus einer räumlichen Baumstruktur wie einem Sparse Voxel
Octree (SVO) hervorgeht.

Der DAG übernimmt dabei nicht die Aufgabe von Dual Contouring.
Stattdessen werden die Verantwortlichkeiten getrennt:

-   **DAG:** Speicherung, Kompression, Hierarchie, Traversal und LOD
-   **Dual Contouring:** Rekonstruktion einer glatten Oberfläche aus den
    volumetrischen Daten
-   **Renderer:** Darstellung der erzeugten Geometrie oder optional
    direktes Raytracing des DAG

Langfristig bietet sich an, nicht nur binäre `solid/empty`-Voxel zu
speichern, sondern einen **Sparse SDF DAG / Field DAG** zu verwenden.

------------------------------------------------------------------------

## 2. Grundidee

Eine klassische Octree-Struktur kann identische Subtrees mehrfach
enthalten:

``` text
Root
├── A
│   └── X
└── B
    └── X
```

In einem DAG werden identische Subtrees dedupliziert:

``` text
Root
├── A ──┐
│       ▼
│       X
│       ▲
└── B ──┘
```

`X` existiert physisch nur einmal im Speicher und kann von mehreren
Eltern referenziert werden.

Dadurch wird die räumliche Hierarchie eines Octrees beibehalten, während
redundante Daten entfernt werden.

------------------------------------------------------------------------

## 3. Zentrale Architektur

``` text
                    WORLD / SOURCE DATA
                           │
                           ▼
                  Sparse Voxel/SDF Tree
                           │
                 Deduplication / Compile
                           │
                           ▼
                    ┌─────────────┐
                    │   SDF DAG   │
                    └──────┬──────┘
                           │
                    DAG Traversal
                           │
          ┌────────────────┼────────────────┐
          │                │                │
         LOD            Culling        Ray Queries
          │                │                │
          └────────────────┼────────────────┘
                           │
                    Surface Detection
                           │
                           ▼
                    Dual Contouring
                           │
                      QEF Vertices
                           │
                    Mesh / Meshlets
                           │
                           ▼
                          GPU
```

Optional kann parallel ein direkter Raytracing-Pfad existieren:

``` text
SDF DAG
├── DAG Ray Traversal → Direct Rendering
└── Dual Contouring → Mesh/Meshlets → Rasterization
```

Damit kann dieselbe Datenbasis für unterschiedliche Renderverfahren
verwendet werden.

------------------------------------------------------------------------

## 4. DAG als zentrale Datenrepräsentation

Ein Node beschreibt primär **was** sich in einem räumlichen Bereich
befindet, nicht **wo** sich dieser Bereich befindet.

Beispiel:

``` cpp
struct DAGNode
{
    uint32_t child[8];
    uint8_t childMask;

    int16_t minSDF;
    int16_t maxSDF;

    uint8_t materialMask;
    uint16_t errorMetric;
};
```

Mögliche Node-Metadaten:

-   Child-Referenzen
-   Child-Mask
-   Minimum/Maximum des SDF
-   Materialinformationen
-   Surface-Mask
-   LOD-/Fehlerwert
-   Normaleninformationen
-   Homogenitätsinformationen

Diese Daten können beim Erstellen des DAG vorberechnet werden.

------------------------------------------------------------------------

## 5. Trennung von Node und räumlicher Instanz

Ein DAG-Node kann an mehreren Positionen der Welt verwendet werden.
Deshalb darf die Weltposition nicht Teil der Identität des Nodes sein.

Die logische Instanz während des Traversals besteht beispielsweise aus:

``` cpp
struct TraversalEntry
{
    uint32_t node;
    ivec3 coordinate;
    uint8_t level;
};
```

Damit gilt:

``` text
DAGNode
= WAS befindet sich hier?

TraversalEntry / Instance
= WO befindet es sich?
```

Eine räumliche Zelle wird daher nicht ausschließlich über eine `NodeID`
identifiziert, sondern über:

``` text
NodeID + Coordinate + Level
```

Das ist insbesondere für Dual Contouring und Nachbarschaftsabfragen
wichtig.

------------------------------------------------------------------------

## 6. Warum ein SDF DAG sinnvoll ist

Ein klassischer Voxel-DAG könnte nur Zustände wie

``` text
EMPTY
SOLID
```

speichern.

Für Dual Contouring ist ein Signed Distance Field jedoch wesentlich
mächtiger:

``` text
SDF < 0  → innerhalb
SDF = 0  → Oberfläche
SDF > 0  → außerhalb
```

Dadurch können glatte Oberflächen rekonstruiert werden, obwohl die
zugrunde liegende Struktur voxelbasiert ist.

Ein Leaf könnte beispielsweise quantisierte SDF-Werte enthalten:

``` cpp
struct Leaf
{
    int16_t sdf[8];
};
```

Je nach Qualitäts- und Speicheranforderungen wären unter anderem
möglich:

-   8-Bit-SDF
-   16-Bit-SDF
-   Float-SDF
-   adaptive/komprimierte Repräsentationen

Ein SDF DAG kann zusätzlich für weitere Systeme verwendet werden:

-   Dual Contouring
-   Raytracing
-   Collision Queries
-   Distance Queries
-   CSG
-   Terrain Editing
-   Physics Sampling
-   LOD

------------------------------------------------------------------------

## 7. Surface Detection

Jeder DAG-Node kann den Wertebereich seines SDF speichern:

``` cpp
node.minSDF;
node.maxSDF;
```

Damit kann sehr schnell entschieden werden, ob ein Node eine Oberfläche
enthalten kann.

### Komplett außerhalb

``` cpp
if (node.minSDF > 0)
{
    // keine Oberfläche
}
```

### Komplett innerhalb

``` cpp
if (node.maxSDF < 0)
{
    // keine Oberfläche
}
```

### Potenzielle Oberfläche

``` cpp
if (node.minSDF <= 0 && node.maxSDF >= 0)
{
    // Oberfläche kann diesen Node durchlaufen
}
```

Nur solche Nodes müssen für Dual Contouring genauer untersucht werden.

Dadurch fungiert der DAG gleichzeitig als Beschleunigungsstruktur.

------------------------------------------------------------------------

## 8. Dual Contouring

Dual Contouring wird nur auf tatsächlich relevanten Surface-Cells
ausgeführt.

Für jede Zelle werden Kanten auf Vorzeichenwechsel untersucht:

``` cpp
if (sign(sdf[a]) != sign(sdf[b]))
{
    // Oberfläche schneidet die Kante
}
```

Die ungefähre Schnittposition kann interpoliert werden:

``` cpp
float t = sdfA / (sdfA - sdfB);

vec3 intersection =
    pA + t * (pB - pA);
```

Aus den Schnittpunkten und Oberflächennormalen wird anschließend über
eine QEF ein Vertex bestimmt:

``` text
min Σ (nᵢ · (x - pᵢ))²
```

Dadurch kann pro Surface-Cell ein Vertex erzeugt werden, der die lokale
Oberfläche möglichst gut approximiert.

------------------------------------------------------------------------

## 9. Positionen nicht im DAG speichern

Da derselbe DAG-Node mehrfach referenziert werden kann, dürfen absolute
Weltpositionen nicht Bestandteil des Nodes sein.

Stattdessen wird die Position während des Traversals bestimmt:

``` cpp
childPosition =
    parentPosition +
    childOffset[child] * parentSize * 0.5;
```

Dual Contouring kann zunächst in lokalen Koordinaten arbeiten:

``` cpp
vec3 worldVertex =
    nodeOrigin +
    localVertex * nodeSize;
```

Dadurch bleibt ein Node unabhängig von seinen Instanzen
wiederverwendbar.

------------------------------------------------------------------------

## 10. Nachbarschaften

Dual Contouring benötigt Informationen über benachbarte Zellen,
insbesondere beim Erzeugen der Flächen zwischen den Vertices.

Bei einem normalen Baum könnten Parent-Pointer verwendet werden. In
einem DAG ist dies problematisch:

``` text
        Parent A
          │
          ▼
       Shared Node
          ▲
          │
        Parent B
```

Ein Node besitzt möglicherweise mehrere Parents.

Deshalb sollte die räumliche Navigation nicht auf einem einzelnen
`node->parent` basieren.

Stattdessen werden Position und Level im Traversal-Kontext mitgeführt:

``` text
NodeID
Coordinate
Level
```

Nachbarschaften werden anschließend über räumliche Koordinaten bzw.
entsprechende Traversal-Algorithmen bestimmt.

------------------------------------------------------------------------

## 11. LOD

Der DAG kann zusätzlich einen vorberechneten Fehlerwert speichern:

``` cpp
node.errorMetric;
```

Beim Rendering kann entschieden werden, ob weiter in den DAG abgestiegen
werden muss:

``` cpp
if (projectedError < threshold)
{
    // diesen Node als LOD-Repräsentation verwenden
}
else
{
    // Children traversieren
}
```

Dadurch kann dieselbe Datenstruktur sowohl die eigentlichen
volumetrischen Daten als auch die LOD-Hierarchie repräsentieren.

Mögliche Faktoren für die LOD-Auswahl:

-   Entfernung zur Kamera
-   projizierte Größe
-   SDF-Fehler
-   Oberflächenkomplexität
-   verfügbare GPU-Zeit
-   gewünschte Bildqualität

------------------------------------------------------------------------

## 12. Caching und wiederverwendbare Berechnungen

Ein DAG kann nicht nur Speicher sparen. Berechnungen, die ausschließlich
vom Inhalt eines Nodes abhängen, können ebenfalls geteilt werden.

Beispiele:

``` text
NodeID
  │
  ├── min/max SDF
  ├── Material-Mask
  ├── Surface-Mask
  ├── Error Metric
  ├── Normal Cone
  └── weitere vorberechnete Metadaten
```

Wird derselbe Node tausendmal referenziert, müssen diese Eigenschaften
trotzdem nur einmal gespeichert bzw. berechnet werden.

Nicht ohne Weiteres teilbar sind dagegen positionsabhängige Ergebnisse
wie:

-   Weltposition
-   Kamera-Sichtbarkeit einer konkreten Instanz
-   absolute Dual-Contouring-Vertexposition
-   Nachbarschaft einer konkreten Instanz

Hierfür bleibt der räumliche Traversal-Kontext notwendig.

------------------------------------------------------------------------

## 13. Editing und Copy-on-Write

Ein Nachteil eines DAG ist die Bearbeitung gemeinsam genutzter Nodes.

Ausgangssituation:

``` text
Shared Node
├── Instance A
├── Instance B
└── Instance C
```

Soll nur `B` verändert werden, darf der gemeinsame Node nicht direkt
überschrieben werden.

Stattdessen wird Copy-on-Write verwendet:

``` text
Shared Node
├── A
└── C

New Modified Node
└── B
```

Danach können entlang des veränderten Pfades neue Parent-Nodes
entstehen.

Optional kann nach Änderungen erneut dedupliziert werden.

------------------------------------------------------------------------

## 14. Empfohlene Authoring-/Runtime-Trennung

Für häufiges Editing ist es möglicherweise sinnvoll, nicht permanent
direkt auf dem maximal komprimierten DAG zu arbeiten.

Empfohlene Pipeline:

``` text
Editable World
     │
     ▼
Sparse Voxel/SDF Tree
     │
     │ compile
     │ deduplicate
     ▼
Runtime SDF DAG
     │
     ├── GPU Traversal
     ├── LOD
     ├── Culling
     ├── Ray Queries
     └── Dual Contouring
```

Vorteile:

-   einfacheres Editing
-   unkompliziertere lokale Änderungen
-   aggressive Runtime-Kompression
-   GPU-freundliches Datenlayout

Für statische oder selten veränderte Welten kann der DAG dagegen direkt
als primäres Runtime-Format verwendet werden.

------------------------------------------------------------------------

## 15. CPU-/GPU-Aufteilung

Eine mögliche spätere Aufteilung wäre:

### CPU

``` text
World Editing
    ↓
SDF Generation
    ↓
Tree Construction
    ↓
DAG Deduplication
    ↓
GPU Upload
```

### GPU

``` text
DAG
 ↓
Traversal
 ↓
LOD + Culling
 ↓
Surface Detection
 ↓
Dual Contouring
 ↓
Meshlets / Geometry
 ↓
Rendering
```

Alternativ:

``` text
DAG
 ↓
Ray Traversal
 ↓
Direct Rendering
```

Welche Teile von Dual Contouring tatsächlich auf CPU oder GPU laufen,
kann später anhand der Performance entschieden werden.

------------------------------------------------------------------------

## 16. Mesh-Generierung

Dual Contouring muss nicht zwingend ein dauerhaftes globales Mesh
erzeugen.

Stattdessen kann die Welt in kleinere Renderbereiche aufgeteilt werden:

``` text
DAG
 ↓
Visible Surface Regions
 ↓
Dual Contouring
 ↓
Temporary Meshes / Meshlets
 ↓
GPU
```

Damit kann Geometrie abhängig von

-   Sichtbarkeit
-   Kamera
-   LOD
-   Änderungen

erzeugt und gecacht werden.

Ein möglicher Cache-Key wäre beispielsweise:

``` text
Spatial Region + LOD + Revision
```

statt nur der `NodeID`, da dieselbe DAG-Struktur an mehreren Positionen
auftreten kann.

------------------------------------------------------------------------

## 17. Erwartete Vorteile

### Speicher

Durch Deduplication identischer Subtrees kann der Speicherbedarf stark
reduziert werden.

Besonders profitieren:

-   große homogene Bereiche
-   wiederholende Geometrien
-   künstliche/strukturierte Welten
-   hierarchisch ähnliche Bereiche

### Traversal

Große leere oder homogene Regionen können früh übersprungen werden.

### Berechnung

Node-invariante Ergebnisse können einmal berechnet und wiederverwendet
werden.

### LOD

Die vorhandene Hierarchie kann direkt für adaptive Detailstufen
verwendet werden.

### Dual Contouring

Nur tatsächlich relevante Surface-Cells müssen verarbeitet werden.

### Erweiterbarkeit

Die gleiche Struktur kann später für

-   Rasterization
-   Raytracing
-   Physics
-   Collision
-   CSG
-   Distance Queries

verwendet werden.

------------------------------------------------------------------------

## 18. Nachteile und Risiken

### DAG-Komplexität

Der DAG ist komplexer als ein einfacher Octree.

### Editing

Gemeinsam genutzte Nodes benötigen Copy-on-Write oder eine separate
Editing-Struktur.

### Nachbarschaftsabfragen

Parent-Pointer funktionieren nicht mehr eindeutig.

### GPU-Divergenz

Irreguläres Traversal kann zu Divergenz und zufälligen Speicherzugriffen
führen.

### Deduplication-Kosten

Das Erstellen des DAG benötigt Hashing bzw. Vergleiche identischer
Subtrees.

### SDF-Deduplication

Bei kontinuierlichen SDF-Werten können kleine numerische Unterschiede
verhindern, dass ansonsten ähnliche Nodes dedupliziert werden.
Quantisierung oder kanonische Repräsentationen können deshalb wichtig
werden.

------------------------------------------------------------------------

## 19. Empfohlene Entwicklungsreihenfolge

### Phase 1 -- Sparse SDF Tree

Zunächst einen funktionierenden Sparse Voxel/SDF Tree implementieren.

Ziele:

-   hierarchische Speicherung
-   SDF-Sampling
-   Surface Detection
-   korrekte räumliche Traversierung

### Phase 2 -- Dual Contouring

Dual Contouring auf dem Tree implementieren:

-   Sign Changes
-   Hermite Data
-   Normalen
-   QEF
-   Vertex-Erzeugung
-   Quad-Erzeugung
-   adaptive Auflösung

### Phase 3 -- DAG Deduplication

Anschließend identische Subtrees zusammenfassen:

``` text
SDF Tree
   ↓
Canonicalization
   ↓
Hashing
   ↓
Deduplication
   ↓
SDF DAG
```

Damit kann überprüft werden, wie stark die realen Daten tatsächlich
komprimiert werden.

### Phase 4 -- DAG-Metadaten

Ergänzen von:

-   min/max SDF
-   Surface-Mask
-   Material-Mask
-   Error Metric
-   LOD-Daten

### Phase 5 -- GPU Traversal

DAG in ein GPU-freundliches lineares Format überführen und Traversal
implementieren.

### Phase 6 -- GPU Surface Pipeline

Surface-Nodes auf der GPU identifizieren und Dual Contouring bzw.
Mesh-/Meshlet-Generierung beschleunigen.

### Phase 7 -- Direkter Raytracing-Pfad

Optional den DAG direkt traversieren und damit eine zweite
Rendering-Methode neben Dual Contouring bereitstellen.

------------------------------------------------------------------------

## 20. Zusammenfassung

Die grundlegende Idee lautet:

> **Der DAG beschreibt und komprimiert das volumetrische Feld. Dual
> Contouring erzeugt daraus bei Bedarf eine renderbare Oberfläche.**

Die Architektur trennt damit Datenrepräsentation und
Oberflächenrekonstruktion:

``` text
                DATA
                 │
                 ▼
            Sparse SDF DAG
                 │
       ┌─────────┼─────────┐
       │         │         │
      LOD     Culling   Ray Queries
       │         │         │
       └─────────┼─────────┘
                 │
          Surface Extraction
                 │
                 ▼
          Dual Contouring
                 │
                 ▼
          Mesh / Meshlets
                 │
                 ▼
              Renderer
```

Der DAG kann dadurch langfristig zur zentralen Datenstruktur des
Renderers werden und gleichzeitig als:

-   komprimierter Speicher,
-   räumliche Hierarchie,
-   LOD-Struktur,
-   Traversal-Struktur,
-   Surface-Beschleunigungsstruktur

dienen.

Für häufig editierte Daten ist eine Trennung zwischen einer einfach
editierbaren Sparse-SDF-Struktur und einem kompilierten Runtime-DAG
sinnvoll. Für statische bzw. selten veränderte Daten kann der DAG direkt
das primäre Runtime-Format darstellen.
