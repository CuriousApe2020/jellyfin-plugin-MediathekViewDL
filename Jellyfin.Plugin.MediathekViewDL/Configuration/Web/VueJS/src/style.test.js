import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { resolve } from 'path'

// This stylesheet is injected into document.head and Jellyfin's web client is a single-page
// app: the <style> element outlives the settings page. Unscoped rules here therefore keep
// applying to Jellyfin's own UI afterwards - and `.card`, `.btn` and `.field` are exactly the
// class names Jellyfin uses, which drew borders around every poster tile on the home page
// until the next full page load. These tests fail on any rule that could escape again.
// Resolved from the vitest root (the VueJS project directory), not from import.meta.url:
// under the happy-dom environment that is not a file: URL.
const css = readFileSync(resolve('src/style.css'), 'utf-8')
    .replace(/\/\*[\s\S]*?\*\//g, '')

const SCOPE = ':where(.mvpl-scope)'

function topLevelRules(source) {
    const rules = []
    let depth = 0
    let buffer = ''
    let selector = ''

    for (const char of source) {
        if (char === '{') {
            depth += 1
            if (depth === 1) {
                selector = buffer.trim()
                buffer = ''
                continue
            }
        } else if (char === '}') {
            depth -= 1
            if (depth === 0) {
                rules.push({ selector, body: buffer })
                buffer = ''
                continue
            }
        }

        buffer += char
    }

    return rules
}

describe('style.css', () => {
    const rules = topLevelRules(css)

    it('parses into rules at all', () => {
        // Guards the test itself: a parser that silently returns nothing would make every
        // assertion below pass no matter what the stylesheet contains.
        expect(rules.length).toBeGreaterThan(10)
    })

    it('confines every selector to .mvpl-scope', () => {
        const escaping = []

        for (const rule of rules) {
            if (rule.selector.startsWith('@')) {
                continue
            }

            for (const part of rule.selector.split(',')) {
                const selector = part.trim()
                if (selector && !selector.startsWith(SCOPE)) {
                    escaping.push(selector)
                }
            }
        }

        expect(escaping).toEqual([])
    })

    it('prefixes every animation name', () => {
        // @keyframes are global wherever the rule sits, so a bare name like "spin" would
        // replace an identically named animation in Jellyfin's own stylesheet.
        const names = [...css.matchAll(/@keyframes\s+([\w-]+)/g)].map((match) => match[1])

        expect(names.length).toBeGreaterThan(0)
        for (const name of names) {
            expect(name).toMatch(/^mvpl-/)
        }
    })

    it('keeps the scope free of specificity, so component styles still win', () => {
        // :where() contributes nothing to specificity. Scoping with a plain `.mvpl-scope`
        // (or the page id) instead would quietly outrank the components' own scoped rules.
        expect(SCOPE.startsWith(':where(')).toBe(true)
        expect(css).not.toMatch(/^\s*\.mvpl-scope\s/m)
        expect(css).not.toContain('#mediathekViewDLForkConfigPage')
    })
})
