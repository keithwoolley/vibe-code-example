// MedTracker JS interop: IndexedDB key-value + looping alarm audio.

(() => {
    const DB_NAME = 'medtracker';
    const STORE = 'kv';
    let dbPromise = null;

    function openDb() {
        if (dbPromise) return dbPromise;
        dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, 1);
            req.onupgradeneeded = () => {
                const db = req.result;
                if (!db.objectStoreNames.contains(STORE)) {
                    db.createObjectStore(STORE);
                }
            };
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        return dbPromise;
    }

    async function kvGet(key) {
        const db = await openDb();
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readonly');
            const req = tx.objectStore(STORE).get(key);
            req.onsuccess = () => resolve(req.result ?? null);
            req.onerror = () => reject(req.error);
        });
    }

    async function kvPut(key, value) {
        const db = await openDb();
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).put(value, key);
            tx.oncomplete = () => resolve(true);
            tx.onerror = () => reject(tx.error);
        });
    }

    // Alarm audio — looped while ringing.
    let audioEl = null;
    function ensureAudio() {
        if (!audioEl) {
            audioEl = new Audio('audio/alarm.wav');
            audioEl.loop = true;
            audioEl.preload = 'auto';
        }
        return audioEl;
    }

    async function alarmStart() {
        const a = ensureAudio();
        try {
            a.currentTime = 0;
            await a.play();
        } catch (err) {
            // Browsers may block autoplay before first user gesture — swallow.
            console.warn('alarm play blocked:', err);
        }
    }

    function alarmStop() {
        if (audioEl) {
            audioEl.pause();
            audioEl.currentTime = 0;
        }
    }

    // Ask the browser for a persistent audio unlock on first user gesture.
    // Blazor can call this when the first interaction happens so later auto-plays succeed.
    async function primeAudio() {
        const a = ensureAudio();
        try {
            a.muted = true;
            await a.play();
            a.pause();
            a.muted = false;
            a.currentTime = 0;
        } catch { /* ignore */ }
    }

    window.medtracker = { kvGet, kvPut, alarmStart, alarmStop, primeAudio };
})();
