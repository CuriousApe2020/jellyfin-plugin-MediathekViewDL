import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import { applyJellyfinTheme } from './utils/jellyfinTheme.js'

let appInstance = null;

function mountApp(container) {
    if (!appInstance) {
        appInstance = createApp(App);
        appInstance.mount(container);
    }
}

function unmountApp() {
    if (appInstance) {
        appInstance.unmount();
        appInstance = null;
    }
}

const pageElementId = 'configPageVueJSPage';
const page = document.getElementById(pageElementId);
const isStandalone = import.meta.env.DEV || !window.Dashboard;

if (page) {
    if (isStandalone) {
        // Standalone / dev-preview: mount immediately
        const container = page.querySelector('#app');
        if (container) {
            applyJellyfinTheme(page).finally(() => mountApp(container));
        }
    } else {
        // Inside Jellyfin: remount on every pageshow so config is reloaded
        page.addEventListener('pageshow', (event) => {
            const container = page.querySelector('#app');
            if (container) {
                // Re-measure the active Jellyfin theme on every pageshow too -
                // the admin may have switched skins since the last visit. Wait
                // for it (a couple of animation frames) before mounting so the
                // first paint already uses the right colors instead of
                // flashing the fallback palette first.
                applyJellyfinTheme(page).finally(() => {
                    // Always remount so onMounted fires and config is fetched fresh
                    unmountApp();
                    mountApp(container);
                });
            }
        });

        page.addEventListener('pagehide', () => {
            unmountApp();
        });
    }
}
