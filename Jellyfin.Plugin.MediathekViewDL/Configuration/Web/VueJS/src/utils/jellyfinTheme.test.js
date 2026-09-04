import { describe, it, expect, afterEach } from 'vitest'
import { applyJellyfinTheme } from './jellyfinTheme.js'

describe('jellyfinTheme', () => {
    afterEach(() => {
        // Reset any inline styles/custom properties the tests put on <body> so cases don't bleed into each other.
        document.body.removeAttribute('style')
    })

    it('AppliesBgAndTextPrimary_FromJellyfinPaletteVariables_WhenBodySetsThem', async () => {
        // Arrange
        document.body.style.setProperty('--jf-palette-background-default', '#18181b')
        document.body.style.setProperty('--jf-palette-text-primary', '#e4e4e7')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-bg')).toBe('rgb(24, 24, 27)')
        expect(root.style.getPropertyValue('--mvpl-text-primary')).toBe('rgb(228, 228, 231)')
    })

    it('AppliesAccent_FromPrimaryMainVariable_WhenBodySetsIt', async () => {
        // Arrange: this is the exact variable + value confirmed live on a real Jellyfin instance
        document.body.style.setProperty('--jf-palette-primary-main', '#00a4dc')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-accent')).toBe('rgb(0, 164, 220)')
    })

    it('PrefersPrimaryDark_OverDerivedShade_ForAccentHover_WhenBodySetsIt', async () => {
        // Arrange
        document.body.style.setProperty('--jf-palette-primary-main', '#00a4dc')
        document.body.style.setProperty('--jf-palette-primary-dark', '#00779e')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert: Jellyfin's own hover shade is used verbatim, not a derived mix
        expect(root.style.getPropertyValue('--mvpl-accent-hover')).toBe('rgb(0, 119, 158)')
    })

    it('DerivesLighterSurface_FromDarkBackground_WhenPaperVariableIsMissing', async () => {
        // Arrange: a dark background (low luminance), no --jf-palette-background-paper set
        document.body.style.setProperty('--jf-palette-background-default', '#18181b')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert: surface should be a lightened variant of the background, not identical to it
        const bg = root.style.getPropertyValue('--mvpl-bg')
        const surface = root.style.getPropertyValue('--mvpl-surface')
        expect(surface).not.toBe('')
        expect(surface).not.toBe(bg)
    })

    it('FallsBackToDefaults_WithoutThrowing_WhenNoJellyfinPaletteVariablesArePresent', async () => {
        // Arrange: no --jf-palette-* custom properties set anywhere (e.g. an older Jellyfin
        // version, or running in the standalone dev preview without Jellyfin's stylesheet)
        const root = document.createElement('div')

        // Act / Assert: must never throw/reject, and must fall back to the built-in default palette
        await applyJellyfinTheme(root)
        expect(root.style.getPropertyValue('--mvpl-bg')).toBe('rgb(24, 24, 27)')
        expect(root.style.getPropertyValue('--mvpl-text-primary')).toBe('rgb(228, 228, 231)')
        expect(root.style.getPropertyValue('--mvpl-accent')).toBe('rgb(0, 164, 220)')
    })

    it('UsesDarkOnAccent_WhenThemeAccentIsLight', async () => {
        // Arrange: a pale accent - white text on this is the "white on white" bug
        document.body.style.setProperty('--jf-palette-primary-main', '#e8f4fb')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-on-accent')).toBe('rgb(24, 24, 27)')
    })

    it('UsesWhiteOnAccent_WhenThemeAccentIsDark', async () => {
        // Arrange: Jellyfin's own default accent
        document.body.style.setProperty('--jf-palette-primary-main', '#00a4dc')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-on-accent')).toBe('rgb(255, 255, 255)')
    })

    it('UsesDarkOnBorder_WhenThemeIsLight', async () => {
        // Arrange: a light theme - its divider is a light gray, and .btn-secondary /
        // .btn-icon:hover put text directly on that divider color.
        document.body.style.setProperty('--jf-palette-background-default', '#ffffff')
        document.body.style.setProperty('--jf-palette-text-primary', '#111111')
        document.body.style.setProperty('--jf-palette-divider', '#e0e0e0')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert: must not be white, or the button label is invisible
        expect(root.style.getPropertyValue('--mvpl-on-border')).toBe('rgb(24, 24, 27)')
    })

    it('UsesWhiteOnBorder_WhenThemeIsDark', async () => {
        // Arrange
        document.body.style.setProperty('--jf-palette-background-default', '#18181b')
        document.body.style.setProperty('--jf-palette-divider', '#3f3f46')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-on-border')).toBe('rgb(255, 255, 255)')
    })

    it('FlattensTranslucentDivider_SoTagsAreNotWhiteOnWhite', async () => {
        // Arrange: Jellyfin's real dark palette - MUI defines the divider with alpha.
        // Dropping that alpha turned it into pure white, and .field-tag paints text
        // directly onto it: a white pill with white text.
        document.body.style.setProperty('--jf-palette-background-default', '#18181b')
        document.body.style.setProperty('--jf-palette-background-paper', '#27272a')
        document.body.style.setProperty('--jf-palette-divider', 'rgba(255, 255, 255, 0.12)')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert: 12% white over the #27272a panel, not white
        expect(root.style.getPropertyValue('--mvpl-border')).toBe('rgb(65, 65, 68)')
        // ... and the readable foreground follows the visible color, so it stays white
        expect(root.style.getPropertyValue('--mvpl-on-border')).toBe('rgb(255, 255, 255)')
    })

    it('FlattensTranslucentSecondaryText_AgainstTheBackground', async () => {
        // Arrange: MUI's dark secondary text tone.
        document.body.style.setProperty('--jf-palette-background-default', '#000000')
        document.body.style.setProperty('--jf-palette-text-secondary', 'rgba(255, 255, 255, 0.7)')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert: 70% white over black, not pure white
        expect(root.style.getPropertyValue('--mvpl-text-secondary')).toBe('rgb(179, 179, 179)')
    })

    it('LeavesOpaqueColorsUntouched', async () => {
        // Arrange: a theme without any alpha - flattening must be a no-op here.
        document.body.style.setProperty('--jf-palette-background-default', '#18181b')
        document.body.style.setProperty('--jf-palette-divider', '#3f3f46')
        document.body.style.setProperty('--jf-palette-primary-main', '#00a4dc')
        const root = document.createElement('div')

        // Act
        await applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-border')).toBe('rgb(63, 63, 70)')
        expect(root.style.getPropertyValue('--mvpl-accent')).toBe('rgb(0, 164, 220)')
    })

    it('NeverThrows_WhenRootIsInvalid', async () => {
        // Arrange: a root that will blow up when we try to set a CSS property on it
        const brokenRoot = {
            style: {
                setProperty: () => {
                    throw new Error('boom')
                },
            },
        }

        // Act / Assert: must resolve, not reject
        await expect(applyJellyfinTheme(brokenRoot)).resolves.toBeUndefined()
    })
})
