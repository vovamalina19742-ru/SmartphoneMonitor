// ==UserScript==
// @name         999.md Smart Smartphone Agent (Page-Agent Suite)
// @namespace    https://github.com/vovamalina19742-ru/SmartphoneMonitor
// @version      1.0.0
// @description  Автономный ИИ-помощник для 999.md: двуязычный анализ состояния смартфонов, детект перекупов, проверка АКБ/Neverlock и авто-запрос IMEI/состояния в чат.
// @author       SmartphoneMonitor Team & Page-Agent
// @match        https://999.md/ru/*
// @match        https://999.md/ro/*
// @grant        GM_xmlhttpRequest
// @grant        GM_setClipboard
// @grant        GM_addStyle
// @run-at       document-idle
// ==/UserScript==

(function () {
    'use strict';

    // 1. Проверяем, что находимся на странице объявления смартфона
    const isAdvertPage = /\/(?:ru|ro)\/\d{5,}/.test(window.location.pathname);
    if (!isAdvertPage) return;

    // 2. Двуязычный словарь эвристик (RU / RO)
    const RULES = {
        battery: [
            /(?:акб|батаре[яеи]|аккумулятор|bateri[ae]|health|bh)\s*[:=-]?\s*(\d{2,3})\s*%/i,
            /(\d{2,3})\s*%\s*(?:акб|baterie|health|bh)/i
        ],
        neverlock: [
            /\b(?:neverlock|never\s*lock|nevarlock|unlocked|sim\s*free|liber\s*in\s*orice\s*retea)\b/i
        ],
        locked: [
            /\b(?:r-?sim|gevey|mdm|i?cloud\s*blocat|rsim|turbo\s*sim)\b/i
        ],
        repairs: [
            /(?:экран|дисплей|ecran|display)\s+(?:менял[сяи]|schimbat|inlocuit|copie|oem)/i,
            /(?:без|nu\s+lucreaza|fara)\s+(?:face\s*id|truetone|true\s*tone|touch\s*id)/i
        ],
        box_complete: [
            /\b(?:коробк[аеи]|комплект|cutie|set\s+complet|pachet\s+complet|documente|garantie|чек)\b/i
        ]
    };

    // 3. Извлечение параметров из DOM 999.md
    function parseListingDetails() {
        const lang = window.location.pathname.startsWith('/ro') ? 'ro' : 'ru';
        const titleEl = document.querySelector('h1') || document.querySelector('.adPage__header__title');
        const descEl = document.querySelector('.adPage__content__description') || document.querySelector('.adPage__content');
        const priceEl = document.querySelector('.adPage__content__price-feature') || document.querySelector('.adPage__header__price');

        const title = titleEl ? titleEl.innerText.trim() : '';
        const description = descEl ? descEl.innerText.trim() : '';
        const priceRaw = priceEl ? priceEl.innerText.trim() : '0';
        const fullText = `${title}\n${description}`;

        // Поиск АКБ
        let battery = null;
        for (const regex of RULES.battery) {
            const m = fullText.match(regex);
            if (m) {
                battery = parseInt(m[1], 10);
                break;
            }
        }

        // Поиск Neverlock vs Lock
        const isNeverlock = RULES.neverlock.some(r => r.test(fullText));
        const isLocked = RULES.locked.some(r => r.test(fullText));

        // Поиск ремонтов / дефектов
        const repairsFound = [];
        for (const r of RULES.repairs) {
            const m = fullText.match(r);
            if (m) repairsFound.push(m[0]);
        }

        // Комплект
        const hasBox = RULES.box_complete.some(r => r.test(fullText));

        // Сбор ссылок на фото (900x900)
        const photoUrls = [];
        document.querySelectorAll('img').forEach(img => {
            const src = img.src || img.getAttribute('data-src') || '';
            if (src.includes('simpalsmedia.com') && src.includes('BoardImages')) {
                const highRes = src.replace(/\/\d+x\d+\//, '/900x900/');
                if (!photoUrls.includes(highRes)) photoUrls.push(highRes);
            }
        });

        // Оценка риска
        let riskScore = 0;
        let riskNotes = [];

        if (isLocked) {
            riskScore += 60;
            riskNotes.push(lang === 'ro' ? '⚠️ Dispozitiv blocat (R-SIM / MDM / iCloud)!' : '⚠️ Залоченное устройство (R-SIM / MDM / iCloud)!');
        }
        if (repairsFound.length > 0) {
            riskScore += 25;
            riskNotes.push(lang === 'ro' ? '🔧 Reparații/Defecte detectate: ' + repairsFound.join(', ') : '🔧 Зафиксирован ремонт/дефект: ' + repairsFound.join(', '));
        }
        if (!battery && (title.toLowerCase().includes('iphone') || fullText.toLowerCase().includes('iphone'))) {
            riskScore += 15;
            riskNotes.push(lang === 'ro' ? '❓ Starea bateriei (%) nu este specificată' : '❓ Процент АКБ не указан в описании');
        }

        return {
            lang,
            title,
            priceRaw,
            battery,
            isNeverlock,
            isLocked,
            repairsFound,
            hasBox,
            photoCount: photoUrls.length,
            photoUrls,
            riskScore,
            riskNotes
        };
    }

    // 4. Инъекция Smart HUD плашки на страницу
    function injectHUD(data) {
        const hud = document.createElement('div');
        hud.id = 'page-agent-999-hud';
        hud.style.cssText = `
            position: fixed;
            bottom: 24px;
            right: 24px;
            z-index: 999999;
            width: 340px;
            background: #18181b;
            color: #f4f4f5;
            border: 1px solid #3f3f46;
            border-radius: 12px;
            padding: 16px;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.5);
            font-size: 13px;
            line-height: 1.4;
        `;

        const statusColor = data.riskScore >= 40 ? '#ef4444' : data.riskScore >= 20 ? '#eab308' : '#22c55e';
        const statusText = data.riskScore >= 40 ? 'Высокий риск ❌' : data.riskScore >= 20 ? 'Внимание ⚠️' : 'Хороший вариант ✅';

        hud.innerHTML = `
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;">
                <span style="font-weight: bold; font-size: 14px; color: #38bdf8;">📱 999.md AI Monitor</span>
                <span style="background: ${statusColor}; color: #000; font-size: 11px; font-weight: bold; padding: 2px 8px; border-radius: 6px;">
                    ${statusText}
                </span>
            </div>
            <div style="margin-bottom: 8px;">
                <b>Цена:</b> ${data.priceRaw}<br>
                <b>АКБ (Battery):</b> ${data.battery ? data.battery + '%' : '<span style="color:#f59e0b">Не указан</span>'}<br>
                <b>Сеть / SIM:</b> ${data.isLocked ? '<span style="color:#ef4444">BLOCKED</span>' : (data.isNeverlock ? '<span style="color:#22c55e">Neverlock</span>' : 'Не указан')}<br>
                <b>Комплект:</b> ${data.hasBox ? 'Коробка/Документы ✅' : 'Только телефон / Не указано'}<br>
                <b>Фотографий:</b> ${data.photoCount} шт.
            </div>
            ${data.riskNotes.length > 0 ? `
                <div style="background: #27272a; padding: 8px; border-radius: 6px; margin-bottom: 10px; font-size: 12px; color: #fca5a5;">
                    ${data.riskNotes.join('<br>')}
                </div>
            ` : ''}
            <div style="display: flex; gap: 8px;">
                <button id="btn-smart-inquiry" style="flex: 1; background: #2563eb; color: #fff; border: none; border-radius: 6px; padding: 8px; cursor: pointer; font-weight: bold; font-size: 12px;">
                    💬 Спросить продавца
                </button>
                <button id="btn-reveal-phone" style="background: #3f3f46; color: #fff; border: none; border-radius: 6px; padding: 8px 12px; cursor: pointer; font-size: 12px;">
                    📞 Номер
                </button>
            </div>
        `;

        document.body.appendChild(hud);

        // Обработчик авто-запроса в чат
        document.getElementById('btn-smart-inquiry').addEventListener('click', () => {
            const inquiryText = data.lang === 'ro'
                ? `Bună ziua! Vă rog să-mi spuneți dacă bateria este originală (${data.battery ? data.battery + '%' : 'cât %'}) și dacă funcționează Face ID/TrueTone? Ați putea trimite un screenshot cu IMEI/Serial Number pentru verificare? Mulțumesc!`
                : `Здравствуйте! Подскажите, пожалуйста, какой точный процент АКБ, менялся ли дисплей/детали и работают ли Face ID/TrueTone? Можете ли прислать скриншот "Об этом устройстве" с серийным номером/IMEI? Спасибо!`;

            if (navigator.clipboard) {
                navigator.clipboard.writeText(inquiryText);
            }

            // Ищем кнопку чата на 999.md
            const msgBtn = document.querySelector('.adPage__content__footer__actions__chat') ||
                           document.querySelector('button[data-chat]') ||
                           document.querySelector('a[href*="chat"]');
            if (msgBtn) {
                msgBtn.click();
            }
            alert('Текст запроса скопирован в буфер обмена!\n\n' + inquiryText);
        });

        // Обработчик раскрытия номера
        document.getElementById('btn-reveal-phone').addEventListener('click', () => {
            const phoneBtn = document.querySelector('.adPage__content__phone-btn') ||
                             document.querySelector('.adPage__content__phone') ||
                             document.querySelector('button.js-phone-show');
            if (phoneBtn) {
                phoneBtn.click();
            }
        });
    }

    // Запуск после отрисовки DOM
    setTimeout(() => {
        const data = parseListingDetails();
        injectHUD(data);
    }, 1200);

})();
