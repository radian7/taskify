// Opens and closes the reconnect dialog (Components/Shared/ReconnectModal.razor) as Blazor's connection changes.
// A separate file, not an inline script, because the Content Security Policy forbids inline scripts.
(function () {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) {
        return;
    }

    modal.addEventListener('components-reconnect-state-changed', function (event) {
        const state = event.detail.state;
        if (state === 'show') {
            if (!modal.open) {
                modal.showModal();
            }
        } else if (state === 'hide') {
            modal.close();
        } else if (state === 'failed') {
            document.addEventListener('visibilitychange', retryWhenVisible);
        } else if (state === 'rejected') {
            location.reload();
        }
    });

    document.getElementById('components-reconnect-button').addEventListener('click', retry);
    document.getElementById('components-resume-button').addEventListener('click', resume);

    function retryWhenVisible() {
        if (document.visibilityState === 'visible') {
            retry();
        }
    }

    async function retry() {
        document.removeEventListener('visibilitychange', retryWhenVisible);
        try {
            const reconnected = await Blazor.reconnect();
            if (!reconnected) {
                await resume();
            }
        } catch (error) {
            modal.classList.replace('components-reconnect-retrying', 'components-reconnect-failed');
            document.addEventListener('visibilitychange', retryWhenVisible);
        }
    }

    async function resume() {
        try {
            if (await Blazor.resumeCircuit()) {
                modal.close();
            } else {
                location.reload();
            }
        } catch (error) {
            modal.classList.replace('components-reconnect-paused', 'components-reconnect-resume-failed');
        }
    }
})();
