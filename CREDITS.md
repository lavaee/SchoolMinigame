# Credits and licences

## Used by the game

| Asset | Author / source | Licence |
| --- | --- | --- |
| **Vazirmatn** (`Assets/Fonts/Vazirmatn-Regular.ttf`) | Saber Rastikerdar — https://github.com/rastikerdar/vazirmatn | SIL Open Font License 1.1 |
| **Amiri** (`Assets/Fonts/Amiri-Regular.ttf`) | Khaled Hosny — https://github.com/alif-type/amiri | SIL Open Font License 1.1 |
| **RTLTMPro** | Pejman Narimani — https://github.com/pnarimani/RTLTMPro | MIT |
| **TextMeshPro** | Unity Technologies | Unity Companion License (Unity package) |

The full licence texts are kept next to the fonts in `Assets/Fonts/Licenses/`.

Amiri is only a fallback: it provides the handful of Quranic and ligature codepoints that Vazirmatn does
not cover, so the font assets never show a missing-glyph box whatever the data files contain.

## Generated, not imported

Every visual in the game is produced at runtime in C#: the window, frame, sill, pebbles, pot, soil,
leaves, blossom, badges, icons, panels and shadows are meshes built from Unity primitives, or sprites
drawn procedurally into textures (`Assets/Scripts/UI/TextureFactory.cs`). No third-party art,
photography, icon set or sound is included, so there is nothing else to attribute.

## Present in the project but not used by the game

These were already in the repository from the initial commit. They are left in place and are not
referenced by any script under `Assets/Scripts`:

* Odin Inspector, Febucci Text Animator, Graphy, vFolders, vHierarchy, vInspector, vTabs,
  Amplify Shader Editor, Sprite Shaders Ultimate, MonKey Commander, Ingame Debug Console.

The RTLTMPro package also ships demonstration fonts and ranges; the game uses Vazirmatn and Amiri
instead, and only the package's runtime shaping code.
