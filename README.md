# LiPicons.Blazor

Hand-drawn icon system for Hospital Information Systems, as a Blazor component.
**1,149 icons · 6 variants · fully offline** — the manifest is embedded in the assembly,
so icons render behind a hospital firewall with no network fetch and no `wwwroot` copy.

```razor
@using LiPicons.Blazor

<LipiIcon Name="empty-state"
          Variant="LipiconVariant.Regular"
          Size="24"
          Color="@null"
          Class="my-extra-class"
          Title="@null" />
```

## Install

```xml
<PackageReference Include="LiPicons.Blazor" Version="1.0.4" />
```

Targets `net10.0`. Depends only on `Microsoft.AspNetCore.Components.Web` — no HIS or
host-app coupling.

## Parameters

| Parameter | Type | Default | Behavior |
|---|---|---|---|
| `Name` | `string` | `""` (required) | Manifest key, e.g. `"empty-state"`, `"filter"`, `"close"`. |
| `Variant` | `LipiconVariant` | `Regular` | Which weight to render. |
| `Size` | `int` | `24` | Pixel width & height (square); the 24×24 viewBox is preserved. |
| `Color` | `string?` | `null` | Null → `currentColor` (inherits text color). Set → used as stroke/fill color. |
| `Class` | `string?` | `null` | Appended to the root `lipicon` class. |
| `Title` | `string?` | `null` | Null → decorative (`aria-hidden`). Set → `role="img"` + `aria-label`. |

### Variants

`Regular` (1.75) · `Bold` (2.25) · `Light` (1.25) · `Thin` (1.00) · `Fill` · `Duotone`.

## Behavior

- **Unknown name never throws.** It renders nothing; in Development it logs
  `LipiIcon: unknown icon name '…'` to `Console.Error`.
- **`currentColor` by default** — tint via CSS, e.g. wrap in an element with
  `color: var(--color-danger)` and the icon picks it up.
- **`Size` scales the box**, the `viewBox` stays `0 0 24 24`.

### Typed names (optional)

```razor
<LipiIcon Name="@LipiconName.EmptyState" />
```

`LipiconName` exposes a compile-time constant for every icon.

### Dev-mode detection

By default, "Development" is inferred from `ASPNETCORE_ENVIRONMENT` /
`DOTNET_ENVIRONMENT`. In Blazor WebAssembly, set it explicitly if you want dev warnings:

```csharp
LiPicons.Blazor.LipiIcon.DevModeOverride = builder.HostEnvironment.IsDevelopment();
```

---

© 2026 imagiQa Healthcare Services Pvt Ltd. LiPi™ and LiPicons™ are trademarks of
imagiQa Healthcare Services Pvt Ltd.
