window.sponsorCheck = {
    copyToClipboard: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }

        // Fallback for non-secure contexts where navigator.clipboard is unavailable.
        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.focus();
        area.select();
        try {
            document.execCommand('copy');
        } finally {
            document.body.removeChild(area);
        }
        return Promise.resolve();
    }
};
