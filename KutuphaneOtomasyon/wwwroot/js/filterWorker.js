// Tarih parse fonksiyonu (DOM kullanmadan)
function parseDate(dateStr) {
    if (dateStr instanceof Date) return dateStr;
    if (!dateStr || typeof dateStr !== 'string') return null;

    const turkishDateRegex = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/;
    const turkishDateTimeRegex = /^(\d{1,2})\.(\d{1,2})\.(\d{4})\s+(\d{1,2}):(\d{2})(:\d{2})?$/;

    let match = dateStr.match(turkishDateTimeRegex);
    if (match) {
        const year = parseInt(match[3]);
        const month = parseInt(match[2]) - 1;
        const day = parseInt(match[1]);
        const hour = parseInt(match[4]);
        const minute = parseInt(match[5]);
        const second = match[6] ? parseInt(match[6].substring(1)) : 0;
        return new Date(year, month, day, hour, minute, second);
    }

    match = dateStr.match(turkishDateRegex);
    if (match) {
        const year = parseInt(match[3]);
        const month = parseInt(match[2]) - 1;
        const day = parseInt(match[1]);
        return new Date(year, month, day);
    }

    const iso = new Date(dateStr);
    return isNaN(iso.getTime()) ? null : iso;
}

// HTML temizleme fonksiyonu (DOM kullanmadan)
function stripHtml(html) {
    return html.replace(/<[^>]*>?/gm, '').trim();
}

self.onmessage = function (e) {
    const { allData, filters } = e.data;
    const filteredData = allData.filter(row => {
        return filters.every(filter => {
            const { index, type, values } = filter;
            const cellValue = row[index];
            const cellText = stripHtml(cellValue); // DOM kullanmadan temizleme

            switch (type) {
                case 'select':
                    return cellText === values[0];
                case 'text':
                    return cellText.toLowerCase().includes(values[0].toLowerCase());
                case 'range':
                    const num = parseFloat(cellText.replace(',', '.')) || 0;
                    const min = parseFloat(values[0]) || -Infinity;
                    const max = parseFloat(values[1]) || Infinity;
                    return num >= min && num <= max;
                case 'date':
                    const date = parseDate(cellText);
                    if (!date) return false;
                    const startDate = values[0] ? new Date(values[0]) : null;
                    const endDate = values[1] ? new Date(values[1]) : null;
                    return (!startDate || date >= startDate) && (!endDate || date <= endDate);
                default:
                    return true;
            }
        });
    });

    self.postMessage({ type: 'result', data: filteredData });
};

// Worker içine ekleyin (isteğe bağlı):
const batchSize = 1000;
for (let i = 0; i < allData.length; i += batchSize) {
    const progress = Math.round((i / allData.length) * 100);
    self.postMessage({ type: 'progress', progress });
}