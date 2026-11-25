# UU mobile viewport calibration, 0.2.48

Windows UU installation is D:\GameViewer\4.48.4.6020. The user has selected iPhone 13 Pro: 1170x2532 pixels, 390-point width. Current 820x1392 logical display is an app-fitted, 1.697-aspect virtual mode, not native phone ratio. Historical ScreenFit removes two 91-point UU toolbar insets.

The new setting near Phone Model supports Classic (91 points each vertical edge), Expanded (45 each), and Full (0). Legacy is the default. Aspect ratio, VDD mode list, mode snapping, widget placement and gesture coordinate scaling all share the same pinned inset. The setting persists but affects only the NEXT display creation to avoid breaking active UU monitor selection.

Regression tests exercise classic equivalence, inset validity, the native phone aspect and exact driver-to-UI mode consistency. This is a calibration option, not automatic UU iPhone viewport detection. Device-side screenshot/visual acceptance is required. Check monitor identity and physical screen 1920x1080, browser page zoom independence, background resume and window restoration. Keep installed 0.2.47 intact until acceptance.
