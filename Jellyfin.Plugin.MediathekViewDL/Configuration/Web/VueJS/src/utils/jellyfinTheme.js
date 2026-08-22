/**
 * Reads live, computed colors from Jellyfin's own dashboard elements and
 * exposes them as CSS custom properties (--mvpl-*) so our stylesheet can
 * blend in with whatever skin the admin currently has active - including
 * genuinely custom/third-party themes.
 *
 * Why measure the DOM instead of reading Jellyfin's theme colors directly:
 * Jellyfin's stable 10.x web client (what this plugin targets) does not
 * expose its theme palette as CSS custom properties at all - every theme
 * (dark/light/custom skins) is a self-contained stylesheet with hardcoded
 * colors. There is therefore no variable we could simply read. Instead we
 * render a couple of Jellyfin's own native elements (an emby-button, an
 * emby-input) off-screen on the current page - which already has the
 * active theme's stylesheet loaded, because we're running inside a normal
 * Jellyfin dashboard page - and read their *computed* styles. That works
 * for any theme, built-in or custom, regardless of how it implements its
 * colors internally.
 *
 * This is inherently best-effort: if Jellyfin's markup/classes change in a
 * future release, or a custom skin styles nothing we probe, we silently
 * fall back to the plugin's existing hardcoded palette (the `var(--x, Y)`
 * fallback values already in style.css) - the settings page must never
 * break because of this.
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

/** Creates a Jellyfin-native element off-screen inside `host`, reads its computed styles, then removes it. */
function probe(host, build) {
  const el = build()
  el.style.position = 'absolute'
  el.style.left = '-9999px'
  el.style.top = '-9999px'
  el.style.visibility = 'hidden'
  el.style.pointerEvents = 'none'
  host.appendChild(el)
  const computed = window.getComputedStyle(el)
  const result = {
    background: parseRgb(computed.backgroundColor),
    color: parseRgb(computed.color),
    borderColor: parseRgb(computed.borderBottomColor),
  }
  host.removeChild(el)
  return result
}

function probeAccentButton(host) {
  return probe(host, () => {
    const form = document.createElement('form')
    const button = document.createElement('button')
    button.setAttribute('is', 'emby-button')
    button.setAttribute('type', 'submit')
    button.className = 'raised button-submit block emby-button'
    button.textContent = 'x'
    form.appendChild(button)
    return form
  }).background
}

function probeInputBorder(host) {
  return probe(host, () => {
    const container = document.createElement('div')
    container.className = 'inputContainer'
    const input = document.createElement('input')
    input.setAttribute('is', 'emby-input')
    input.setAttribute('type', 'text')
    container.appendChild(input)
    return container
  }).borderColor
}

/**
 * Measures the active Jellyfin theme and sets --mvpl-* custom properties
 * on `root` (should be an ancestor of everything this plugin renders, so
 * the variables cascade to our components without ever leaking into the
 * rest of the Jellyfin dashboard).
 */
export function applyJellyfinTheme(root) {
  try {
    const host = document.body
    const bodyStyle = window.getComputedStyle(host)
    // Reject a fully/mostly transparent read the same way the border/accent
    // probes do below - an unstyled <body> (no Jellyfin CSS loaded, e.g. in
    // tests) reports "rgba(0, 0, 0, 0)", which must fall back to the
    // defaults rather than being treated as "the theme's background is
    // black".
    const bgProbe = parseRgb(bodyStyle.backgroundColor)
    const textProbe = parseRgb(bodyStyle.color)
    const bg = bgProbe && bgProbe.a > 0.05 ? bgProbe : { r: 24, g: 24, b: 27, a: 1 }
    const text = textProbe && textProbe.a > 0.05 ? textProbe : { r: 228, g: 228, b: 231, a: 1 }
    const isDark = relativeLuminance(bg) < 0.5

    const accent = probeAccentButton(host)
    const border = probeInputBorder(host)

    const surfaceTint = isDark ? { r: 255, g: 255, b: 255 } : { r: 0, g: 0, b: 0 }
    const sunkenTint = isDark ? { r: 0, g: 0, b: 0 } : { r: 255, g: 255, b: 255 }

    const vars = {
      '--mvpl-bg': toCss(bg),
      '--mvpl-bg-sunken': toCss(mix(bg, sunkenTint, 0.35)),
      '--mvpl-surface': toCss(mix(bg, surfaceTint, 0.06)),
      '--mvpl-text-primary': toCss(text),
      '--mvpl-text-secondary': toCss(mix(text, bg, 0.35)),
      '--mvpl-text-muted': toCss(mix(text, bg, 0.55)),
    }

    // Only override the border/accent tokens if the probe produced a
    // visible (non-transparent) color - a failed probe (e.g. Jellyfin
    // markup changed) must not overwrite a sane fallback with black.
    if (border && border.a > 0.05) {
      vars['--mvpl-border'] = toCss(border)
      vars['--mvpl-border-hover'] = toCss(mix(border, surfaceTint, 0.3))
    }
    if (accent && accent.a > 0.05) {
      vars['--mvpl-accent'] = toCss(accent)
      vars['--mvpl-accent-hover'] = toCss(mix(accent, sunkenTint, 0.15))
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
