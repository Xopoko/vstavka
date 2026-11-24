# Design system

Vstavka is a quiet utility: a panel docked to the bottom of the screen, a
first-run guide and a settings window. Its interface is built in code, and
every visual decision comes from one of three layers.

| Layer | What it holds | Where |
| --- | --- | --- |
| Scales | The raw steps: colour pairs, sizes, distances | `PanelTheme.cs`, `DesignTokens.cs` |
| Roles | Names that say what a value is for | Brush keys from `PanelTheme`; `TextSize`, `IconSize`, `Space`, `Radius`, `ControlSize`, `Stroke`, `DesktopMotion` |
| Components | Controls assembled from roles | `PanelStyles.axaml`, `PanelVisuals`, `SetupVisuals` |

A view picks a role. It does not write a size, a radius or a colour of its
own. The one exception is the placement of content in a window that is laid
out by hand, such as the top offset of a guide step.

## Colour

`PanelTheme.Apply` computes every brush for the current theme, accent and
contrast setting and publishes it under a key. Colours are written there and
nowhere else.

| Key | Use |
| --- | --- |
| `WindowBrush` | Window and panel background, dialogs, messages |
| `SurfaceBrush` | Cards and grouped content |
| `InputBrush` | Fields |
| `MediaBrush` | The backdrop of an image or a colour that could not be read |
| `HoverBrush`, `PressedBrush` | A control under the pointer and while pressed |
| `SelectionBrush` | The active filter, selected text |
| `SelectedSurfaceBrush` | The selected card, tinted tiles and buttons |
| `ScrimBrush` | The layer between the panel and a dialog |
| `InkBrush` | Text |
| `MutedBrush` | Secondary text and resting icons |
| `AccentBrush` | Accent used as text, icon or ring |
| `LineBrush`, `HoverLineBrush` | Borders and dividers, at rest and under the pointer |
| `PrimaryBrush`, `PrimaryHoverBrush`, `PrimaryInkBrush` | The main action and the text on it |
| `SuccessBrush`, `WarningBrush` | Status text and dots |
| `DangerBrush`, `DangerLineBrush`, `DangerSurfaceBrush` | A destructive action before it is confirmed |
| `DangerFillBrush`, `DangerHoverBrush`, `DangerPressedBrush`, `OnDangerBrush` | The confirming button and the text on it |
| `TextTypeBrush`, `LinkTypeBrush`, `ImageTypeBrush`, `ColorTypeBrush`, `FilesTypeBrush` | The type icon of a card |
| `CollectionBlue` … `CollectionSlate` | The eight collection colours |

The accent is the system colour or one of five presets. Used as text it is
shifted until it reads at 4.5:1 on every background it can meet; used as a
fill it is darkened until white text reads on it. With the system's high
contrast setting, every role collapses to ink on surface.

## Type

The interface uses the system font in two weights, regular and semibold. A
copied colour value is set in a monospaced stack, and the modifier keys of a
Mac shortcut in a symbol stack.

| Role | Size | Line | Use |
| --- | --- | --- | --- |
| `TextSize.Caption` | 12 | 16 | Labels, status, anything secondary |
| `TextSize.Body` | 14 | 20 | The default |
| `TextSize.Subtitle` | 18 | font | The title of a section, a dialog or an empty state; a card's headline |
| `TextSize.Keycap` | 22 | font | The large keycaps in the guide |
| `TextSize.Title` | 24 | 30 | The title of a guide step |
| `TextSize.Display` | 28 | 34 | The title of the step where the guide ends |

## Icons

Icons are Fluent System Icons, regular; the filled variant marks a state
that is on, such as a pinned card.

| Role | Size | Use |
| --- | --- | --- |
| `IconSize.Inline` | 14 | Beside caption text |
| `IconSize.Control` | 16 | In buttons and fields, beside body text |
| `IconSize.Feature` | 20 | Alone in a tile |
| `IconSize.Hero` | 28 | An empty state |

## Space

Distances sit on a 4-point grid, with 2 and 6 for dense rows.

| Role | Value | Use |
| --- | --- | --- |
| `Space.Xxs` | 2 | A label and the line under it |
| `Space.Xs` | 4 | Parts of one object, such as the keys of a shortcut |
| `Space.S` | 6 | An icon and its label |
| `Space.M` | 8 | Neighbouring controls, the rows of a form |
| `Space.L` | 12 | The inside of a surface |
| `Space.Xl` | 16 | Groups, and the edge of the panel |
| `Space.Xxl` | 24 | Sections |
| `Space.Xxxl` | 32 | The edge of a window |

## Shape

Each radius holds the one below it: the panel holds surfaces, a surface
holds controls.

| Role | Value | Use |
| --- | --- | --- |
| `Radius.Small` | 4 | Keycaps, tags, thumbnails |
| `Radius.Control` | 8 | Buttons, fields, tiles, media inside a card |
| `Radius.Surface` | 12 | Cards, grouped content, dialogs, messages |
| `Radius.Panel` | 24 | The panel |
| `Radius.Pill(height)` | height / 2 | Dots, switches, filter chips |

Controls are 36 high (`ControlSize.Default`), or 26 when they sit inside a
row of text (`ControlSize.Compact`). Borders and dividers are 1 wide
(`Stroke.Hairline`); focus and selection rings are 2 (`Stroke.Focus`).
Nothing casts a shadow: depth comes from the surface colours and hairlines.

## Motion

`DesktopMotion` holds the durations and the curve. A change animates only
when the movement explains it; whatever repeats all day is instant, and
nothing the user asked for waits for an animation. The system setting for
animations turns all of it off.

| Role | Duration | Use |
| --- | --- | --- |
| `Exit` | 83 ms | Something leaves because the user sent it away |
| `Enter` | 167 ms | Something arrives, or changes state where it stands |
| `Travel` | 250 ms | Content moves across the window |
| `Scene` | 500 ms | An illustration in the guide |

## Components

### Button

`Button.panel-button`, built with `SetupVisuals.Button` or
`PanelVisuals.IconButton`. One more class picks the variant.

| Variant | Background | Text | Border | Use |
| --- | --- | --- | --- | --- |
| `ghost` | none | muted | none | Cancel, Back, Skip |
| `secondary` | surface | ink | line | An action beside the main one |
| `primary` | primary | primary ink, semibold | none | The one main action of a window |
| `danger` | surface | danger | danger line | Starts a destructive action |
| `danger-filled` | danger fill | on danger, semibold | none | Confirms it |
| `setup-tinted` | selected surface | accent | none | The way forward from a blocked step |
| `permission` | none | accent, caption | line | An action inside a row; compact height |
| `chip` | none | ink | none | A filter; pill-shaped |
| `icon-button` | none | muted | none | A 36 × 36 button with a control-size icon and a tooltip |

| State | Change |
| --- | --- |
| Hover | Hover background and ink text. `secondary` also darkens its border; `permission` keeps its accent text; filled and tinted variants take their own hover fill |
| Pressed | Pressed background; `danger-filled` takes its pressed fill |
| Focus (keyboard) | A 2-wide accent ring; outside the edge on filled variants |
| Disabled | The variant's own colours at 45% opacity |
| Active (`chip.active`) | Selection background, accent text, semibold; unchanged under the pointer |

Fluent's button theme recolours the same template part on hover, press and
disable. One rule in `PanelStyles.axaml` keeps the part following the
button, so a variant's states are exactly the setters written for it.

### Card

One history item, `HistoryCard` in `PanelStyles.axaml` with `ClipCardView`
inside. Text is inset by `Space.L` and media by `Space.M`.

| State | Change |
| --- | --- |
| Rest | Surface background, hairline border, `Radius.Surface` |
| Hover | Hover line border |
| Selected | Selected surface background and a 2-wide accent ring |
| Focus (keyboard) | The same ring |

### Field, select and switch

| Component | Rest | Hover and focus | Keyboard focus |
| --- | --- | --- | --- |
| Field (`TextBox.panel-search`, with `setup-input` for forms) | Input background, hairline border, `Radius.Control` | Hover line border | 2-wide accent border |
| Select (`ComboBox.setup-select`) | The same | Fluent's own | 2-wide accent ring |
| Switch (`ToggleButton.setup-switch`) | Hover-coloured track, muted knob | — | 2-wide accent ring around the track |

A switch that is on has a primary track and a white knob; the knob slides
between the two ends. A disabled field, like a disabled button, keeps its
colours at 45% opacity.

### Smaller parts

| Part | Built with | Notes |
| --- | --- | --- |
| Surface | `SetupVisuals.Surface` | Background, hairline and radius in one; `Radius.Surface` unless told otherwise |
| Keycap | `ShortcutRecorder` | Small (24 high, caption) and large (60 high, `TextSize.Keycap`); the thicker bottom edge is the side of the key |
| Key hint | `PanelVisuals.Hint` | A flat keycap with its action, in the panel's footer |
| Tag | `SetupVisuals.Surface` with `Radius.Small` | Caption text on the window colour |
| Dot | `PanelVisuals.Dot` | Status and collection colour; 8 across |
| Icon tile | `SetupVisuals.Surface` with `Radius.Control` | A control-size icon on the selected surface colour |
| Message | `PanelVisuals.Feedback` | Appears at once, fades when it leaves on its own |
| Dialog | `PanelWindow.Collections` | A surface on the window colour over the scrim; Esc closes it and focus returns to what opened it |
| Empty state | `PanelWindow.ShowEmpty` | A hero icon, a subtitle and one line of body text |

## Changing the system

- Use a role. If none fits, the change belongs in the scale or in a shared
  component, not in the view.
- A new colour is a new key in `PanelTheme`, with a value for the light and
  the dark theme and a decision for high contrast.
- `Vstavka.Desktop.Tests` walks the panel, the guide and the settings window
  and fails when a text size, icon size, radius or gap on screen is not on
  its scale.
- Check light, dark and right-to-left renders after a change:
  `dotnet run --project tests/Vstavka.Desktop.Tests -- --render <folder>`.
