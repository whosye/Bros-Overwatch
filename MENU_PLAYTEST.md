# Menu and server browser

The runtime UI is built by `MainMenuUI`, `MainMenuHost`, `ServerBrowserUI`, and `SettingsMenuUI`.
`MenuStyle` owns the cottage palette and rounded controls without changing the combat HUD.
The backdrop is an illustrative static image; its generation prompt is in `tools/menu-art`.

- Main menu: create a game, servers, settings, quit; small shared rename and music controls.
- Host: player/server names, map, mode, mode-specific win rule, advanced test cooldowns. Creates a lobby; teams, heroes, and bots remain in the lobby.
- Servers: local saved addresses and LAN queries, manual add/remove, pagination, last successful connection. Names are shared across all pre-connection forms.
- Settings: game, sound, graphics, controls; changes apply immediately and persist on close. Escape closes settings. Multiplayer keeps running.
- Connecting: the attempt can be cancelled.

## Discovery and persistence

UDP game traffic still uses 7777. Status queries use host UDP 47777 and an ephemeral client reply port. Allow the host executable through the firewall. All players should run the updated build; BROS2 request/reply discovery replaces BROS1 periodic advertisements.

Opening the browser requests one refresh. Repeated clicks, tab switches, and reopening within five seconds reuse cached results. There is no recurring network refresh. Replies have a two-second timeout. Unknown addresses stay unverified until queried; nonresponding saved entries remain in the list, and direct connection remains available even if discovery is blocked.

The server bounds receive processing and reply rates. Replies must match the current query nonce. The browser stores at most 128 IPv4 endpoints in `servers.history.v1` PlayerPrefs. It saves successful visits only after both the local player and match state exist. A manual add does not become the last visited server. Online states never persist across app launches.

## Validation

Unity 6000.5.9f1:

1. Build the separate development client: `-batchmode -nographics -quit -executeMethod MenuPlaytest.BuildClient`.
2. Run the editor host test with graphics: `-batchmode -executeMethod MenuPlaytest.Run`.

Both commands need `-projectPath` pointing to `OverwatchBros` and a suitable `-logFile`.
The test saves screenshots, logs, and results in ignored `artifacts/Menu`.
It preserves and restores the local settings it changes. It checks endpoint validation, deduplication and persistence, unsuccessful-connection history, refresh throttling (including reopening), actual UDP status replies, host rule propagation, and real-process late joining/team choice in both modes.

`MenuJoinProbe` is dormant unless explicitly invoked with its test flag and is excluded from release builds.

A normal release build can be made with `-executeMethod MenuPlaytest.BuildGame`. Output: `artifacts/Menu/Build/BrosOverwatch.exe` and its adjacent data folder.

## Compact revision

Main actions are smaller and grouped together. Host and browser panels use 80% / 82% of their original footprint; settings use 80%. The controls tab uses separate keycaps and action columns. A drawn note with a single diagonal stroke represents muted music.

The menu uses Barlow Medium for body text and Barlow Condensed SemiBold for display text, sourced from the Google Fonts repository under the included SIL Open Font Licenses. Font setup is local to the menu and does not replace HUD typography.

All three maps have illustrated previews. Hanamura and Dust II are LAN-party interpretations; prompts and visual-reference links are in `tools/menu-art/map-thumbnails.prompts.txt`.

Use `BrosOverwatch > Menu > Capture Compact Previews` to render the interface in an isolated editor preview scene without entering Play mode, opening/saving the current scene, or exiting Unity. It also verifies fonts, map previews, and control-label overflow. Captures go to `artifacts/Menu/compact`. For a disposable validation checkout, `-executeMethod MenuPreview.ValidateAndBuild` opens that checkout's sample scene, captures the UI, and builds the game.

## Lobby revision

Lobby uses the shared cottage palette, compact team rosters, a scrollable hero grid, an embedded 3D hero preview, and separate Match / Abilities / Bots tabs. Hero clicks only select; F2 opens the embedded abilities tab in the lobby. The explicit in-match F2 reference remains available. Abilities reuse the existing hero reference data, with wrapped descriptions and scrollable content. Team rosters also scroll instead of overlapping other controls.

`BrosOverwatch > Menu > Review Lobby Layout` captures a read-only preview scene and checks ability text for every hero at 1080p/720p. The editor-only fixture uses portraits in place of the live 3D showcase. A fixed-purpose `artifacts/Menu/lobby-review.request` file requests this review and a Windows build on the next editor domain reload, without closing Unity or saving/switching the user's scene.
