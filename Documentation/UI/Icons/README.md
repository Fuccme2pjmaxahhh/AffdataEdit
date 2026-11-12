# Tabler Outline UI icons

Source: [Tabler Icons v3.49.0](https://github.com/tabler/tabler-icons/tree/v3.49.0/icons/outline), distributed under the [MIT license](LICENSE). The SVG files retain the upstream style; `folder-sync`, `mouse-right`, `user-bust` and `clock` are local adaptations using the same 24-unit, rounded 2-unit strokes.

## Image mappings

Existing PNG paths and GUIDs are retained so scene and prefab references remain valid. `MDI` is a legacy directory name. These PNGs use white strokes on a transparent background and keep their original dimensions; Unity UI tint supplies the theme color.

| Unity asset | Tabler source |
| --- | --- |
| `Assets/Textures/Icon/MDI/Folder.png` | [`folder-open`](folder-open.svg) |
| `Assets/Textures/Icon/MDI/Save.png` | [`device-floppy`](device-floppy.svg) |
| `Assets/Textures/Icon/MDI/FileSync.png` | [`folder-sync`](folder-sync.svg) |
| `Assets/Resources/AffdataEdit/Icons/CloudUpload.png` | [`cloud-upload`](cloud-upload.svg) |
| `Assets/Resources/AffdataEdit/Icons/Realtime.png` | [`refresh-dot`](refresh-dot.svg) |
| `Assets/Resources/AffdataEdit/Icons/Account.png` | [`user-bust`](user-bust.svg) |
| `Assets/Resources/AffdataEdit/Icons/Edit.png` | [`pencil`](pencil.svg) |
| `Assets/Resources/AffdataEdit/Icons/Spectrum.png` | [Local spectrum bars](spectrum.svg): three outlined bars built like Tabler `chart-bar`, same 24-unit 2-unit-stroke style |
| `Assets/Textures/Icon/MDI/Undo.png` | [`arrow-back-up`](arrow-back-up.svg) |
| `Assets/Textures/Icon/MDI/Redo.png` | [`arrow-forward-up`](arrow-forward-up.svg) |
| `Assets/Textures/Icon/MDI/Refresh.png` | [`refresh`](refresh.svg) |
| `Assets/Textures/Icon/MDI/Timeline.png` | [`clock`](clock.svg) |
| `Assets/Textures/Icon/MDI/Swap.png` | [`arrows-exchange`](arrows-exchange.svg) |
| `Assets/Textures/Icon/MDI/Grid.png` | [`grid-3x3`](grid-3x3.svg) |
| `Assets/Textures/Icon/MDI/Check.png` | [`bug`](bug.svg) |
| `Assets/Textures/Icon/MDI/Camera.png` | [`camera`](camera.svg) |
| `Assets/Textures/Icon/MDI/Volumn.png` | [`volume`](volume.svg) |
| `Assets/Textures/Icon/MDI/Skin.png` | [`shirt`](shirt.svg) |
| `Assets/Textures/Icon/MDI/Hotkey.png` | [`keyboard`](keyboard.svg) |
| `Assets/Textures/Icon/MDI/Setting.png` | [`settings`](settings.svg) |
| `Assets/Textures/Icon/MDI/Power.png` | [`power`](power.svg) |
| `Assets/Resources/AffdataEdit/Icons/Checkmark.png` | [`check`](check.svg) |
| `Assets/Resources/AffdataEdit/Icons/Close.png` | [`x`](x.svg) |
| `Assets/Resources/AffdataEdit/Icons/ChevronDown.png` | [`chevron-down`](chevron-down.svg) |

File watching uses a folder with a lower-right sync badge. Settings uses [`mouse-right`](mouse-right.svg), rendered to `Assets/Resources/AffdataEdit/Icons/MouseRight.png`, with a solid right button.

## Inline symbols

`Assets/Resources/AffdataEdit/Icons/TablerSymbols.asset` is a TextMesh Pro sprite asset backed by `TablerSymbols.png`. The atlas contains eight 128 × 128 cells, ordered left to right. Private-use Unicode characters allow the icons to work with plain-text labels without enabling rich-text parsing. Dynamic labels use `AdeUiIcons.SetText`; scene and prefab labels reference the same sprite asset and tint sprites with their text color.

| Unicode | Tabler source | Use |
| --- | --- | --- |
| `U+E000` | [`plus`](plus.svg) | Add |
| `U+E001` | [`minus`](minus.svg) | Remove timing group |
| `U+E002` | [`x`](x.svg) | Remove row / close |
| `U+E003` | [`arrows-move`](arrows-move.svg) | Reselect value |
| `U+E004` | [`chevron-right`](chevron-right.svg) | Collapsed section |
| `U+E005` | [`chevron-down`](chevron-down.svg) | Expanded section |
| `U+E006` | [`info-circle`](info-circle.svg) | About |
| `U+E007` | [`check`](check.svg) | Selected appearance |

## Playback controls

The final symbol groups in `../Hud/Play.svg`, `PlayPressed.svg`, `Pause.svg`, and `PausePressed.svg` use `player-play` / `player-pause`. Their button silhouettes and labels are retained. The corresponding PNGs under `Assets/Resources/AffdataEdit/Theme/Dark/PlayPause` retain their existing dimensions.

OBS, application branding, and Note skin artwork are unchanged.
