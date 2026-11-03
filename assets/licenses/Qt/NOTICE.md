# Qt runtime notices

Takao dynamically links unmodified Qt 6.8.3 Core, Gui, Widgets, Network and Svg
libraries and deploys the Windows platform/image plugins with windeployqt.
Qt is copyright The Qt Company Ltd. and its contributors. The open source
libraries are used under LGPL-3.0; the accompanying GPL-3.0 text forms part of
the LGPL terms. Individual bundled components retain the licenses recorded in
the supplied SPDX manifests and license texts.

Exact QtBase source: https://github.com/qt/qtbase/tree/c07c2d5a527a644d36e7853d55132ae38921682f

QtSvg source: https://github.com/qt/qtsvg/tree/v6.8.3

Matching complete Qt source archives: https://download.qt.io/archive/qt/6.8/6.8.3/submodules/

The Qt DLLs are separate replaceable files. Takao does not prohibit reverse
engineering for debugging modifications to these LGPL libraries. No Qt source
changes were made. Build instructions and pinned archive checksums are in
scripts/install-qt-sdk.ps1 and scripts/build.ps1 in the Takao source repository.
