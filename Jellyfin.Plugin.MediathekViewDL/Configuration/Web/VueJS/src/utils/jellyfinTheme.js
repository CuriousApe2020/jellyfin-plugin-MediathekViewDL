/**
 * Reads Jellyfin's own theme-palette CSS custom properties (--jf-palette-*)
 * and re-exposes the ones we need as our own --mvpl-* custom properties, so
 * our stylesheet blends in with whatever skin the admin currently has
 * active - including genuinely custom/third-party themes.
 *
 * Earlier versions of this file measured the *computed* styles of a couple
 * of off-screen Jellyfin elements (an emby-button, an emby-input) instead,
 * based on the assumption that Jellyfin's stable 10.x web client does not
 * expose its theme palette as CSS custom properties at all. That assumption
 * was wrong (at least as of 10.11): Jellyfin's web client (MUI-based) sets a
 * full set of `--jf-palette-*` custom properties on <body> -
 * `--jf-palette-primary-main`, `--jf-palette-background-default`,
 * `--jf-palette-text-primary`, etc. - confirmed both in jellyfin-web's own
 * source (src/themes/_base/_theme.scss reads every one of its colors via
 * `var(--jf-palette-X, $fallback)`) and live, in the browser, against a real
 * Jellyfin 10.11 instance. Reading these directly is simpler and more
 * accurate than probing rendered elements, so the DOM-probing approach was
 * removed.
 *
 * This is still best-effort: if a given variable is missing (an older
 * Jellyfin version, or a custom skin that doesn't set it), we fall back to
 * the plugin's existing hardcoded palette (the `var(--x, Y)` fallback
 * values already in style.css) - the settings page must never break
 * because of this.
 */

function parseRgb(colorString) {
  const match = colorString && colorString.match(/rgba?\(([^)]+)\)/)
  if (!match) {
    return null
  }
  const parts = match[1].split(',').map((part) => parseFloat(part.trim()))
  if (parts.length < 3 || parts.slice(0, 3).some(Number.isNaN)) {
    return null
  }
  const alpha = parts.length > 3 ? parts[3] : 1
  return { r: parts[0], g: parts[1], b: parts[2], a: Number.isNaN(alpha) ? 1 : alpha }
}

function toCss(color) {
  return `rgb(${Math.round(color.r)}, ${Math.round(color.g)}, ${Math.round(color.b)})`
}

function mix(from, to, weight) {
  return {
    r: from.r + (to.r - from.r) * weight,
    g: from.g + (to.g - from.g) * weight,
    b: from.b + (to.b - from.b) * weight,
  }
}

function relativeLuminance(color) {
  const channel = (v) => {
    const c = v / 255
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b)
}

/**
 * Foreground for text/icons sitting *on top of* `background`: white, unless
 * that background is itself light, in which case a near-black.
 *
 * Several of our controls sit on a theme-derived background (the accent
 * color, the divider color) whose lightness can't be known in advance -
 * hardcoding `color: white` there is what made buttons unreadable ("white on
 * white") on light themes.
 *
 * Deliberately a lightness test rather than "pick the higher WCAG contrast
 * ratio": Jellyfin's own default accent (#00a4dc) actually scores better with
 * dark text, but Jellyfin itself renders white on it, and blending in with the
 * host UI is the whole point of this module. So white stays the default and we
 * only flip when it would genuinely be unreadable. The 0.5 threshold matches
 * the `isDark` test used for the rest of the palette below.
 */
function contrastText(background) {
  const white = { r: 255, g: 255, b: 255 }
  // The plugin's darkest palette tone rather than pure black, so the result
  // still looks at home in the surrounding design.
  const nearBlack = { r: 24, g: 24, b: 27 }
  return relativeLuminance(background) < 0.5 ? white : nearBlack
}

/** Parses a `#rgb`/`#rgba`/`#rrggbb`/`#rrggbbaa` hex color string into { r, g, b, a }, or null if malformed. */
function parseHex(hex) {
  const clean = hex.slice(1)
  let r
  let g
  let b
  let a = 1
  if (clean.length === 3 || clean.length === 4) {
    r = parseInt(clean[0] + clean[0], 16)
    g = parseInt(clean[1] + clean[1], 16)
    b = parseInt(clean[2] + clean[2], 16)
    if (clean.length === 4) {
      a = parseInt(clean[3] + clean[3], 16) / 255
    }
  } else if (clean.length === 6 || clean.length === 8) {
    r = parseInt(clean.slice(0, 2), 16)
    g = parseInt(clean.slice(2, 4), 16)
    b = parseInt(clean.slice(4, 6), 16)
    if (clean.length === 8) {
      a = parseInt(clean.slice(6, 8), 16) / 255
    }
  } else {
    return null
  }
  if ([r, g, b].some(Number.isNaN)) {
    return null
  }
  return { r, g, b, a: Number.isNaN(a) ? 1 : a }
}

/**
 * Normalizes a CSS color string into an { r, g, b, a } object, or null if
 * empty/unrecognized. Jellyfin's own theme SCSS defines its palette as hex
 * literals (confirmed both in jellyfin-web's source and live in the
 * browser), and re-exposes them as CSS custom properties verbatim - i.e.
 * without normalizing them to rgb()/rgba() - so hex is the format we
 * actually need to handle; rgb()/rgba() is supported too in case a custom
 * skin defines its palette that way instead.
 */
function parseColor(value) {
  if (!value) {
    return null
  }
  const trimmed = value.trim()
  if (trimmed.startsWith('#')) {
    return parseHex(trimmed)
  }
  return parseRgb(trimmed)
}

/** Reads a `--jf-palette-*` custom property from Jellyfin's live theme, normalized to { r, g, b, a }, or null if unset/invalid. */
function readJellyfinColor(name) {
  const raw = window.getComputedStyle(document.body).getPropertyValue(name)
  return parseColor(raw && raw.trim())
}

/**
 * Measures the active Jellyfin theme (via its --jf-palette-* CSS custom
 * properties) and sets --mvpl-* custom properties on `root` (should be an
 * ancestor of everything this plugin renders, so the variables cascade to
 * our components without ever leaking into the rest of the Jellyfin
 * dashboard). Kept async for compatibility with existing callers that
 * already await/chain this - the read itself is now fully synchronous, no
 * animation frames needed.
 */
export async function applyJellyfinTheme(root) {
  try {
    const DEFAULT_BG = { r: 24, g: 24, b: 27, a: 1 }
    const DEFAULT_TEXT = { r: 228, g: 228, b: 231, a: 1 }
    // Jellyfin's own default (non-custom) theme color - #00a4dc, MUI's $primary-main in
    // jellyfin-web's base palette - used only as a last resort when --jf-palette-primary-main
    // itself is unavailable (e.g. standalone dev preview with no Jellyfin body to read from).
    const DEFAULT_ACCENT = { r: 0, g: 164, b: 220, a: 1 }

    const bg = readJellyfinColor('--jf-palette-background-default') || DEFAULT_BG
    const text = readJellyfinColor('--jf-palette-text-primary') || DEFAULT_TEXT
    const isDark = relativeLuminance(bg) < 0.5
    const surfaceTint = isDark ? { r: 255, g: 255, b: 255 } : { r: 0, g: 0, b: 0 }
    const sunkenTint = isDark ? { r: 0, g: 0, b: 0 } : { r: 255, g: 255, b: 255 }

    const surface = readJellyfinColor('--jf-palette-background-paper') || mix(bg, surfaceTint, 0.06)
    const textSecondary = readJellyfinColor('--jf-palette-text-secondary') || mix(text, bg, 0.35)
    // Jellyfin doesn't expose a dedicated "muted" text tone - always derive it from
    // whichever secondary tone we ended up with (measured or fallback).
    const textMuted = mix(textSecondary, bg, 0.35)
    const divider = readJellyfinColor('--jf-palette-divider') || mix(bg, surfaceTint, 0.2)
    const accent = readJellyfinColor('--jf-palette-primary-main') || DEFAULT_ACCENT
    // Jellyfin's own stylesheet already uses --jf-palette-primary-dark as the
    // hover/active shade for this same button style (.button-submit:hover), so
    // prefer it over deriving our own darkened variant.
    const accentHover = readJellyfinColor('--jf-palette-primary-dark') || mix(accent, sunkenTint, 0.15)

    const vars = {
      '--mvpl-bg': toCss(bg),
      '--mvpl-bg-sunken': toCss(mix(bg, sunkenTint, 0.35)),
      '--mvpl-surface': toCss(surface),
      '--mvpl-text-primary': toCss(text),
      '--mvpl-text-secondary': toCss(textSecondary),
      '--mvpl-text-muted': toCss(textMuted),
      '--mvpl-border': toCss(divider),
      '--mvpl-border-hover': toCss(mix(divider, surfaceTint, 0.3)),
      '--mvpl-accent': toCss(accent),
      '--mvpl-accent-hover': toCss(accentHover),
      // Readable foreground for the two theme-derived backgrounds we put text/icons on.
      // Never hardcode `white` against these: on a light theme the divider is a light
      // gray and a light accent is possible too, which is what made these controls
      // unreadable before.
      '--mvpl-on-accent': toCss(contrastText(accent)),
      '--mvpl-on-border': toCss(contrastText(divider)),
    }

    for (const [key, value] of Object.entries(vars)) {
      root.style.setProperty(key, value)
    }
  } catch (error) {
    // Never let theme detection break the settings page.
    // eslint-disable-next-line no-console
    console.error('[MediathekViewDLFork] Jellyfin-Theme-Erkennung fehlgeschlagen, verwende Standardfarben.', error)
  }
}
