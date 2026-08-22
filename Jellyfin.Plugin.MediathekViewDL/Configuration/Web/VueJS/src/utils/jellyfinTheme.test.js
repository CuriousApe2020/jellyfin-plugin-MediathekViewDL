import { describe, it, expect, afterEach } from 'vitest'
import { applyJellyfinTheme } from './jellyfinTheme.js'

describe('jellyfinTheme', () => {
    afterEach(() => {
        // Reset any inline styles the tests put on <body> so cases don't bleed into each other.
        document.body.removeAttribute('style')
    })

    it('AppliesBgAndTextPrimary_FromBodyComputedStyle_WhenBodyIsStyled', () => {
        // Arrange
        document.body.style.backgroundColor = 'rgb(24, 24, 27)'
        document.body.style.color = 'rgb(228, 228, 231)'
        const root = document.createElement('div')

        // Act
        applyJellyfinTheme(root)

        // Assert
        expect(root.style.getPropertyValue('--mvpl-bg')).toBe('rgb(24, 24, 27)')
        expect(root.style.getPropertyValue('--mvpl-text-primary')).toBe('rgb(228, 228, 231)')
    })

    it('DerivesLighterSurface_FromDarkBackground_WhenThemeIsDark', () => {
        // Arrange: a dark background (low luminance)
        document.body.style.backgroundColor = 'rgb(24, 24, 27)'
        document.body.style.color = 'rgb(228, 228, 231)'
        const root = document.createElement('div')

        // Act
        applyJellyfinTheme(root)

        // Assert: surface should be a lightened variant of the background, not identical to it
        const bg = root.style.getPropertyValue('--mvpl-bg')
        const surface = root.style.getPropertyValue('--mvpl-surface')
        expect(surface).not.toBe('')
        expect(surface).not.toBe(bg)
    })

    it('FallsBackToDefaults_WithoutThrowing_WhenBodyHasNoComputedColors', () => {
        // Arrange: no styles set anywhere - computed backgroundColor/color come back
        // fully transparent ("rgba(0, 0, 0, 0)"), which must not be read as "the theme
        // background is black".
        const root = document.createElement('div')

        // Act / Assert: must never throw, and must fall back to the built-in default palette
        expect(() => applyJellyfinTheme(root)).not.toThrow()
        expect(root.style.getPropertyValue('--mvpl-bg')).toBe('rgb(24, 24, 27)')
        expect(root.style.getPropertyValue('--mvpl-text-primary')).toBe('rgb(228, 228, 231)')
    })

    it('NeverThrows_WhenRootIsInvalid', () => {
        // Arrange: a root that will blow up when we try to set a CSS property on it
        const brokenRoot = {
            style: {
                setProperty: () => {
                    throw new Error('boom')
                },
            },
        }

        // Act / Assert
        expect(() => applyJellyfinTheme(brokenRoot)).not.toThrow()
    })
})
