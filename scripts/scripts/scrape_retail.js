const fs = require('fs');
const path = require('path');
const https = require('https');
const dns = require('dns');
const { execSync } = require('child_process');

const outputFile = 'd:\\Создание программ\\Antygravity\\reports\\retail_promo_prices.json';

// Ensure output dir exists
const dir = path.dirname(outputFile);
if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
}

// User Agent to look like a browser
const headers = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
    'Accept-Language': 'ro,ru;q=0.9,en;q=0.8',
    'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8',
    'Connection': 'keep-alive'
};

// Custom DNS Resolver for Maximum.md (bypasses local DNS failure)
dns.setServers(['8.8.8.8', '1.1.1.1']);

function customLookup(hostname, options, callback) {
    let cb = callback;
    let opt = options;
    if (typeof options === 'function') {
        cb = options;
        opt = {};
    }
    dns.resolve4(hostname, (err, addresses) => {
        if (err) {
            return cb(err);
        }
        if (!addresses || addresses.length === 0) {
            return cb(new Error(`No addresses found for ${hostname}`));
        }
        if (opt.all) {
            cb(null, [{ address: addresses[0], family: 4 }]);
        } else {
            cb(null, addresses[0], 4);
        }
    });
}

function fetchUrl(url, useDnsOverride = false) {
    return new Promise((resolve, reject) => {
        const fetchOptions = { headers };
        if (useDnsOverride) {
            fetchOptions.lookup = customLookup;
        }
        https.get(url, fetchOptions, (res) => {
            if (res.statusCode === 301 || res.statusCode === 302) {
                return fetchUrl(res.headers.location, useDnsOverride).then(resolve).catch(reject);
            }
            if (res.statusCode !== 200) {
                return reject(new Error(`Failed with status ${res.statusCode} for ${url}`));
            }
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve(data));
        }).on('error', reject);
    });
}

function fetchJson(url, useDnsOverride = false) {
    return new Promise((resolve, reject) => {
        const parsedUrl = new URL(url);
        const fetchOptions = {
            hostname: parsedUrl.hostname,
            path: parsedUrl.pathname + parsedUrl.search,
            method: 'GET',
            headers: {
                ...headers,
                'Accept': 'application/json, text/plain, */*',
                'Referer': 'https://orange.md/',
                'Origin': 'https://orange.md'
            }
        };

        if (useDnsOverride) {
            fetchOptions.lookup = customLookup;
        }

        const req = https.request(fetchOptions, (res) => {
            if (res.statusCode === 301 || res.statusCode === 302) {
                const location = res.headers.location ? new URL(res.headers.location, url).toString() : null;
                if (location) {
                    return fetchJson(location, useDnsOverride).then(resolve).catch(reject);
                }
            }
            if (res.statusCode !== 200) {
                return reject(new Error(`Failed with status ${res.statusCode} for ${url}`));
            }

            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                try {
                    resolve(JSON.parse(data));
                } catch (err) {
                    reject(new Error(`Invalid JSON from ${url}: ${err.message}`));
                }
            });
        });

        req.on('error', reject);
        req.end();
    });
}

function isProductLikeObject(obj) {
    if (!obj || typeof obj !== 'object') {
        return false;
    }
    const keys = Object.keys(obj).map(k => k.toLowerCase());
    const productKeys = ['name', 'title', 'price', 'discount', 'brand', 'url', 'slug', 'productname'];
    return productKeys.some(key => keys.includes(key));
}

function findProductArray(data) {
    if (Array.isArray(data)) {
        if (data.some(item => isProductLikeObject(item))) {
            return data;
        }
        return null;
    }
    if (data && typeof data === 'object') {
        for (const key of Object.keys(data)) {
            const result = findProductArray(data[key]);
            if (result) {
                return result;
            }
        }
    }
    return null;
}

async function fetchOrangeProducts(targetBrands) {
    const baseUrl = 'https://www.orange.md/shop/bff/api/v2/catalog/1582/';
    const pageSize = 100;
    let page = 1;
    const products = [];

    while (true) {
        const url = `${baseUrl}?PageSize=${pageSize}&PageNumber=${page}`;
        let response;
        try {
            response = await fetchJson(url, true);
        } catch (e) {
            console.error(`Orange.md page ${page} fetch failed:`, e.message);
            break;
        }

        const items = response?.device || [];
        if (!Array.isArray(items) || items.length === 0) {
            break;
        }

        for (const item of items) {
            const name = (item.name || '').trim();
            if (!name) continue;

            const rawBrand = item.brandName || '';
            let matchedBrand = null;
            for (const brand of targetBrands) {
                if (rawBrand.toLowerCase().includes(brand.toLowerCase()) || name.toLowerCase().includes(brand.toLowerCase())) {
                    matchedBrand = brand;
                    break;
                }
            }
            if (!matchedBrand) continue;

            const priceObj = item.price || {};
            const price = parseFloat(priceObj.price) || 0;
            const oldPrice = parseFloat(priceObj.old) || price;
            const discount = priceObj.discount && typeof priceObj.discount.amount === 'number' ? priceObj.discount.amount : (oldPrice > price ? oldPrice - price : 0);

            if (discount <= 0) continue;

            let productUrl = item.href || '';
            if (productUrl && !productUrl.startsWith('http')) {
                productUrl = `https://www.orange.md${productUrl}`;
            }

            const storage = parseMemory(name);

            products.push({
                Shop: 'Orange',
                Brand: matchedBrand,
                Name: name,
                StorageGB: storage,
                Price: price,
                OldPrice: oldPrice,
                Discount: discount,
                Url: productUrl || 'https://www.orange.md/ro/shop/catalog/oferte/toate-reducerile'
            });
        }

        if (items.length < pageSize) {
            break;
        }
        page++;
    }

    return products;
}

function fetchBomba() {
    try {
        const cmd = `curl.exe -s -A "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" -H "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8" -H "Accept-Language: ro,ru;q=0.9,en;q=0.8" "https://bomba.md/ro/category/telefoane-mobile-686094/"`;
        const html = execSync(cmd, { encoding: 'utf8', maxBuffer: 15 * 1024 * 1024 });
        return html;
    } catch (e) {
        console.error("Bomba fetch failed:", e.message);
        return "";
    }
}

// Entity decoder
function decodeHtml(text) {
    return text
        .replace(/&quot;/g, '"')
        .replace(/&amp;/g, '&')
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
        .replace(/&#39;/g, "'");
}

function parseMemory(text) {
    const match = text.match(/(\d+)\s*(GB|GB\s*RAM|TB)/i);
    if (match) {
        const val = parseInt(match[1]);
        const unit = match[2].toUpperCase();
        return unit.includes('TB') ? val * 1024 : val;
    }
    return 0;
}

const darwinUrls = [
    { brand: 'Apple', url: 'https://darwin.md/telefoane/smartphone/apple-iphone' },
    { brand: 'Xiaomi', url: 'https://darwin.md/telefoane/smartphone/xiaomi' },
    { brand: 'Samsung', url: 'https://darwin.md/telefoane/smartphone/samsung' }
];

const enterUrls = [
    { brand: 'Apple', url: 'https://enter.online/telefoane/smartphone-uri/apple' },
    { brand: 'Xiaomi', url: 'https://enter.online/telefoane/smartphone-uri/xiaomi' },
    { brand: 'Samsung', url: 'https://enter.online/telefoane/smartphone-uri/samsung' }
];

async function main() {
    const allProducts = [];
    const targetBrands = ['Apple', 'Xiaomi', 'Samsung'];

    console.log("=== ЗАПУСК ПАРСЕРА КРУПНЫХ МАГАЗИНОВ (Darwin, Enter, Maximum, Bomba) ===\n");

    // 1. Scrape Darwin
    for (const item of darwinUrls) {
        try {
            console.log(`Scraping Darwin: ${item.brand}...`);
            const html = await fetchUrl(item.url);
            const regex = /data-ga4="({[^"]+})"/g;
            let match;
            let count = 0;
            while ((match = regex.exec(html)) !== null) {
                try {
                    const decoded = decodeHtml(match[1]);
                    const obj = JSON.parse(decoded);
                    if (obj && obj.ecommerce && obj.ecommerce.items && obj.ecommerce.items[0]) {
                        const product = obj.ecommerce.items[0];
                        const name = product.item_name;
                        const price = parseFloat(product.price) || 0;
                        const discount = parseFloat(product.discount) || 0;
                        const oldPrice = price + discount;
                        const storage = parseMemory(name) || parseMemory(product.item_variant || '');

                        if (discount > 0) {
                            allProducts.push({
                                Shop: 'Darwin',
                                Brand: item.brand,
                                Name: name.trim(),
                                StorageGB: storage,
                                Price: price,
                                OldPrice: oldPrice,
                                Discount: discount,
                                Url: item.url
                            });
                            count++;
                        }
                    }
                } catch (e) {}
            }
            console.log(`-> Found ${count} products with discounts for ${item.brand} in Darwin`);
        } catch (err) {
            console.error(`Failed to scrape Darwin ${item.brand}: ${err.message}`);
        }
    }

    // 2. Scrape Enter
    for (const item of enterUrls) {
        try {
            console.log(`Scraping Enter.online: ${item.brand}...`);
            const html = await fetchUrl(item.url);
            const regexBroad = /data-gtm="({[^"]+})"/g;
            let match;
            let count = 0;
            while ((match = regexBroad.exec(html)) !== null) {
                try {
                    const decoded = decodeHtml(match[1]);
                    const obj = JSON.parse(decoded);
                    if (obj && obj.ecommerce && obj.ecommerce.items && obj.ecommerce.items[0]) {
                        const product = obj.ecommerce.items[0];
                        const name = product.item_name;
                        const price = parseFloat(product.price) || 0;
                        const discount = parseFloat(product.discount) || 0;
                        const oldPrice = price + discount;
                        const storage = parseMemory(name) || parseMemory(product.item_variant || '');

                        if (discount > 0) {
                            allProducts.push({
                                Shop: 'Enter',
                                Brand: item.brand,
                                Name: name.trim(),
                                StorageGB: storage,
                                Price: price,
                                OldPrice: oldPrice,
                                Discount: discount,
                                Url: item.url
                            });
                            count++;
                        }
                    }
                } catch (e) {}
            }
            console.log(`-> Found ${count} products with discounts for ${item.brand} in Enter.online`);
        } catch (err) {
            console.error(`Failed to scrape Enter ${item.brand}: ${err.message}`);
        }
    }

    // 3. Scrape Maximum
    try {
        console.log(`Scraping Maximum.md...`);
        const maxUrl = 'https://maximum.md/ro/telefoane-si-gadgeturi/telefoane-si-comunicatii/smartphoneuri/';
        const html = await fetchUrl(maxUrl, true);
        const parts = html.split('class="js-content product__item');
        let count = 0;
        for (let i = 1; i < parts.length; i++) {
            const part = parts[i];
            const titleMatch = part.match(/class="product__item__title"[^>]*>\s*<a href="([^"]+)"[^>]*>\s*([^<]+)\s*<\/a>/);
            if (!titleMatch) continue;
            
            const name = titleMatch[2].trim();
            const url = "https://maximum.md" + titleMatch[1];
            
            // Detect brand
            let matchedBrand = null;
            for (const brand of targetBrands) {
                if (name.toLowerCase().includes(brand.toLowerCase())) {
                    matchedBrand = brand;
                    break;
                }
            }
            if (!matchedBrand) continue;

            const currentPriceMatch = part.match(/class="product__item__price-current"[^>]*>\s*<span>\s*([\d\s]+)/);
            if (!currentPriceMatch) continue;
            const price = parseFloat(currentPriceMatch[1].replace(/\s+/g, ''));
            
            const oldPriceMatch = part.match(/class="product__item__price-old"[^>]*>\s*<span>\s*([\d\s]+)/);
            let oldPrice = 0;
            if (oldPriceMatch) {
                oldPrice = parseFloat(oldPriceMatch[1].replace(/\s+/g, ''));
            }
            
            const discount = oldPrice > price ? (oldPrice - price) : 0;
            const storage = parseMemory(name);

            if (discount > 0) {
                allProducts.push({
                    Shop: 'Maximum',
                    Brand: matchedBrand,
                    Name: name,
                    StorageGB: storage,
                    Price: price,
                    OldPrice: oldPrice,
                    Discount: discount,
                    Url: url
                });
                count++;
            }
        }
        console.log(`-> Found ${count} products with discounts in Maximum.md`);
    } catch (err) {
        console.error(`Failed to scrape Maximum.md: ${err.message}`);
    }

    // 4. Scrape Bomba
    try {
        console.log(`Scraping Bomba.md...`);
        const html = fetchBomba();
        const tagRegex = /<a href="(\/ro\/product\/[^"]+)"[^>]*class="name ecommerce-list-data"[^>]*>/g;
        let match;
        let count = 0;
        while ((match = tagRegex.exec(html)) !== null) {
            const fullTag = match[0];
            const path = match[1];
            const url = "https://bomba.md" + path;
            
            const brandMatch = fullTag.match(/data-ecom_brand="([^"]*)"/);
            const titleMatch = fullTag.match(/title="([^"]*)"/);
            const priceMatch = fullTag.match(/data-ecom_price="([^"]*)"/);
            const discountMatch = fullTag.match(/data-ecom_discount="([^"]*)"/);
            
            if (brandMatch && titleMatch && priceMatch) {
                let brand = brandMatch[1];
                // Match brand to our target brands format
                let matchedBrand = null;
                for (const targetBrand of targetBrands) {
                    if (brand.toLowerCase() === targetBrand.toLowerCase()) {
                        matchedBrand = targetBrand;
                        break;
                    }
                }
                if (!matchedBrand) continue;

                const name = titleMatch[1];
                const price = parseFloat(priceMatch[1]) || 0;
                const discount = discountMatch ? parseFloat(discountMatch[1]) || 0 : 0;
                const oldPrice = price + discount;
                const storage = parseMemory(name);
                
                if (discount > 0) {
                    allProducts.push({
                        Shop: 'Bomba',
                        Brand: matchedBrand,
                        Name: name,
                        StorageGB: storage,
                        Price: price,
                        OldPrice: oldPrice,
                        Discount: discount,
                        Url: url
                    });
                    count++;
                }
            }
        }
        console.log(`-> Found ${count} products with discounts in Bomba.md`);
    } catch (err) {
        console.error(`Failed to scrape Bomba.md: ${err.message}`);
    }

    // 5. Scrape Moldcell
    try {
        console.log(`Scraping Moldcell.md...`);
        let page = 1;
        let count = 0;
        while (true) {
            const url = `https://eshop.moldcell.md/ro/telefoane/smartphone-uri?page=${page}`;
            let html = '';
            try {
                html = await fetchUrl(url, true);
            } catch (e) {
                // If page fetch fails, stop paginating
                break;
            }

            const regex = /<script[^>]*type="application\/ld\+json"[^>]*>([\s\S]*?)<\/script>/gi;
            let match;
            let pageFoundCount = 0;

            while ((match = regex.exec(html)) !== null) {
                try {
                    const obj = JSON.parse(match[1]);
                    if (obj['@type'] === 'CollectionPage' && obj.mainEntityOfPage && obj.mainEntityOfPage.itemListElement) {
                        const list = obj.mainEntityOfPage.itemListElement;
                        for (const elem of list) {
                            const prod = elem.item;
                            if (prod && prod['@type'] === 'Product') {
                                const name = prod.name;
                                
                                // Detect brand
                                let matchedBrand = null;
                                for (const brand of targetBrands) {
                                    if (name.toLowerCase().includes(brand.toLowerCase())) {
                                        matchedBrand = brand;
                                        break;
                                    }
                                }
                                if (!matchedBrand) continue;

                                const offers = prod.offers;
                                if (offers) {
                                    const originalPrice = parseFloat(offers.price) || 0;
                                    let price = originalPrice;
                                    if (offers.priceSpecification && typeof offers.priceSpecification.price === 'number') {
                                        price = offers.priceSpecification.price;
                                    }
                                    const discount = originalPrice > price ? (originalPrice - price) : 0;
                                    const prodUrl = offers.url ? offers.url.replace('http://eshop-frontend:3000', 'https://eshop.moldcell.md') : '';
                                    const storage = parseMemory(name);

                                    if (discount > 0) {
                                        allProducts.push({
                                            Shop: 'Moldcell',
                                            Brand: matchedBrand,
                                            Name: name.trim(),
                                            StorageGB: storage,
                                            Price: price,
                                            OldPrice: originalPrice,
                                            Discount: discount,
                                            Url: prodUrl
                                        });
                                        count++;
                                    }
                                    pageFoundCount++;
                                }
                            }
                        }
                    }
                } catch (e) {}
            }

            // If no products were found on this page, stop paginating
            if (pageFoundCount === 0) {
                break;
            }
            page++;
        }
        console.log(`-> Found ${count} products with discounts in Moldcell.md`);
    } catch (err) {
        console.error(`Failed to scrape Moldcell.md: ${err.message}`);
    }

    // 6. Scrape Orange
    try {
        console.log(`Scraping Orange.md...`);
        const orangeProducts = await fetchOrangeProducts(targetBrands);
        orangeProducts.forEach(product => allProducts.push(product));
        console.log(`-> Found ${orangeProducts.length} products with discounts in Orange.md`);
    } catch (err) {
        console.error(`Failed to scrape Orange.md: ${err.message}`);
    }

    // Save output
    fs.writeFileSync(outputFile, JSON.stringify(allProducts, null, 4), 'utf8');
    console.log(`\n✅ Успешно сохранено ${allProducts.length} записей в ${outputFile}`);
}

main().catch(console.error);

