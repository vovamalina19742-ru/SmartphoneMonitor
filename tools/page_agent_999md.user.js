// ==UserScript==
// @name         999.md Smart Smartphone Agent (Page-Agent Suite)
// @namespace    https://github.com/vovamalina19742-ru/SmartphoneMonitor
// @version      1.2.0
// @description  Автономный ИИ-помощник для 999.md: двуязычный анализ состояния смартфонов, детект подделок/копий, дефектов, АКБ/Neverlock и авто-запрос IMEI/состояния в чат.
// @author       SmartphoneMonitor Team & Page-Agent
// @match        *://999.md/*
// @match        *://*.999.md/*
// @grant        GM_xmlhttpRequest
// @grant        GM_setClipboard
// @grant        GM_addStyle
// @run-at       document-idle
// ==/UserScript==

(function () {
    'use strict';

    // Двуязычный словарь эвристик (RU / RO)
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
        fake: [
            /\b(?:поддельн\w+|копи[яеи]|реплик\w+|fake|replica|copie|1:1|android\s+ios|китайск\w+)\b/i
        ],
        damaged: [
            /\b(?:разбит\w+|трещин\w+|треснут\w+|побит\w+|spart\w*|fisurat\w*|defect\w*|cracked|broken|на\s+запчаст\w+)\b/i
        ],
        repairs: [
            /(?:экран|дисплей|ecran|display|стекло|sticl[ae])\s+(?:менял[сяи]|schimbat|inlocuit|copie|oem|spart)/i,
            /(?:без|nu\s+lucreaza|fara)\s+(?:face\s*id|truetone|true\s*tone|touch\s*id)/i
        ],
        box_complete: [
            /\b(?:коробк[аеи]|комплект|cutie|set\s+complet|pachet\s+complet|documente|garantie|чек)\b/i
        ]
    };

    function isAdvertUrl() {
        return /\/\d{6,}/.test(window.location.pathname);
    }

    function parseListingDetails() {
        const lang = window.location.pathname.startsWith('/ro') ? 'ro' : 'ru';
        
        // Универсальный поиск заголовка, описания и цены в разных версиях верстки 999.md
        const titleEl = document.querySelector('h1') || 
                        document.querySelector('.adPage__header__title') ||
                        document.querySelector('[data-qa="ad-title"]');
                        
        const descEl = document.querySelector('.adPage__content__description') || 
                       document.querySelector('.adPage__content') ||
                       document.querySelector('[data-qa="ad-description"]') ||
                       document.querySelector('article');

        // Поиск цены: проверяем стандартные элементы и текстовые блоки с валютой
        let priceRaw = 'По договоренности';
        const priceHeader = document.querySelector('.adPage__content__price-feature') || 
                            document.querySelector('.adPage__header__price') ||
                            document.querySelector('.ad-price');
        
        if (priceHeader && priceHeader.innerText.trim()) {
            priceRaw = priceHeader.innerText.trim();
        } else {
            // Ищем первый крупный блок с ценой MDL / EUR / $
            const allElements = Array.from(document.querySelectorAll('h1, h2, h3, div, span'));
            for (const el of allElements) {
                const t = el.innerText ? el.innerText.trim() : '';
                if (/^\d[\d\s]{1,10}\s*(?:MDL|lei|EUR|€|\$|USD)/i.test(t) && t.length < 25 && el.children.length === 0) {
                    priceRaw = t;
                    break;
                }
            }
        }

        const title = titleEl ? titleEl.innerText.trim() : document.title;
        const description = descEl ? descEl.innerText.trim() : '';
        // Включаем весь видимый текст страницы для максимального охвата
        const fullText = `${title}\n${description}\n${document.body ? document.body.innerText : ''}`;

        // Поиск АКБ
        let battery = null;
        for (const regex of RULES.battery) {
            const m = fullText.match(regex);
            if (m) {
                battery = parseInt(m[1], 10);
                break;
            }
        }

        // Проверка Neverlock vs Залочен
        const isNeverlock = RULES.neverlock.some(r => r.test(fullText));
        const isLocked = RULES.locked.some(r => r.test(fullText));

        // Проверка на ПОДДЕЛКУ / РЕПЛИКУ
        const isFake = RULES.fake.some(r => r.test(fullText));

        // Проверка на ПОВРЕЖДЕНИЯ (битый/трещины)
        const isDamaged = RULES.damaged.some(r => r.test(fullText));

        // Наличие ремонтов / дефектов
        const hasRepairs = RULES.repairs.some(r => r.test(fullText));

        // Комплектность
        const hasBox = RULES.box_complete.some(r => r.test(fullText));

        // Подсчет фото
        const photoEls = document.querySelectorAll('.adPage__content__photos img, .gallery-item img, [data-qa="gallery-image"], img[src*="i.simpalsmedia.com"]');
        const photoCount = Math.max(photoEls.length, 1);

        // Расчет риск-скора (0 - 100, чем выше тем подозрительнее)
        let riskScore = 0;
        const riskNotes = [];

        if (isFake) {
            riskScore += 95;
            riskNotes.push('🚫 ПОДДЕЛКА / РЕПЛИКА (Fake/Copy)');
        }
        if (isDamaged) {
            riskScore += 45;
            riskNotes.push('💥 Разбито / Трещины / Дефект корпуса');
        }
        if (isLocked) {
            riskScore += 50;
            riskNotes.push('⚠️ Залочен / R-SIM / MDM');
        }
        if (hasRepairs) {
            riskScore += 30;
            riskNotes.push('🔧 Ремонт / Замена запчастей');
        }
        if (battery !== null && battery < 80) {
            riskScore += 25;
            riskNotes.push(`🔋 Износ батареи: ${battery}%`);
        }
        if (photoCount <= 2) {
            riskScore += 15;
            riskNotes.push('📷 Мало фотографий (2 или меньше)');
        }
        if (!hasBox && !isFake) {
            riskScore += 10;
            riskNotes.push('📦 Нет коробки / документов в описании');
        }

        return {
            title,
            description,
            priceRaw,
            battery,
            isNeverlock,
            isLocked,
            hasRepairs,
            hasBox,
            photoCount,
            riskScore,
            riskNotes,
            lang
        };
    }

    function injectHUD(data) {
        let hud = document.getElementById('page-agent-999-hud');
        if (!hud) {
            hud = document.createElement('div');
            hud.id = 'page-agent-999-hud';
            document.body.appendChild(hud);
        }

        hud.style.cssText = `
            position: fixed;
            bottom: 20px;
            right: 20px;
            z-index: 2147483647;
            width: 330px;
            background: #18181b;
            color: #f4f4f5;
            border: 1px solid #3f3f46;
            border-radius: 12px;
            padding: 14px;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.65);
            font-size: 13px;
            line-height: 1.4;
            backdrop-filter: blur(8px);
        `;

        const statusColor = data.riskScore >= 40 ? '#ef4444' : data.riskScore >= 20 ? '#eab308' : '#22c55e';
        const statusText = data.riskScore >= 40 ? 'Высокий риск ❌' : data.riskScore >= 20 ? 'Внимание ⚠️' : 'Хороший вариант ✅';

        hud.innerHTML = `
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                <span style="font-weight: bold; font-size: 13px; color: #38bdf8; display: flex; align-items: center; gap: 4px;">
                    📱 999.md AI Monitor
                </span>
                <span style="background: ${statusColor}; color: #000; font-size: 11px; font-weight: bold; padding: 2px 7px; border-radius: 6px;">
                    ${statusText}
                </span>
            </div>
            <div style="margin-bottom: 8px; font-size: 12px; color: #d4d4d8;">
                <b>Цена:</b> ${data.priceRaw}<br>
                <b>АКБ:</b> ${data.battery ? data.battery + '%' : '<span style="color:#f59e0b">Не указан в тексте</span>'}<br>
                <b>Сеть / SIM:</b> ${data.isLocked ? '<span style="color:#ef4444; font-weight: bold;">BLOCKED</span>' : (data.isNeverlock ? '<span style="color:#22c55e">Neverlock</span>' : 'Не указан')}<br>
                <b>Комплект:</b> ${data.hasBox ? 'Коробка ✅' : 'Без коробки / Не указано'}<br>
                <b>Фотографий:</b> ${data.photoCount} шт.
            </div>
            ${data.riskNotes.length > 0 ? `
                <div style="background: #27272a; padding: 6px 8px; border-radius: 6px; margin-bottom: 8px; font-size: 11px; color: #fca5a5; border-left: 3px solid #ef4444;">
                    ${data.riskNotes.join('<br>')}
                </div>
            ` : ''}
            <div style="display: flex; gap: 6px;">
                <button id="btn-smart-inquiry" style="flex: 1; background: #2563eb; color: #fff; border: none; border-radius: 6px; padding: 7px; cursor: pointer; font-weight: bold; font-size: 11px;">
                    💬 Скопировать вопрос продавцу
                </button>
                <button id="btn-close-hud" style="background: #3f3f46; color: #fff; border: none; border-radius: 6px; padding: 7px 10px; cursor: pointer; font-size: 11px;">
                    ✕
                </button>
            </div>
        `;

        // Обработчик закрытия
        document.getElementById('btn-close-hud').addEventListener('click', () => {
            hud.style.display = 'none';
        });

        // Обработчик авто-запроса в чат
        document.getElementById('btn-smart-inquiry').addEventListener('click', () => {
            const inquiryText = data.lang === 'ro'
                ? `Bună ziua! Vă rog să-mi spuneți dacă bateria este originală (${data.battery ? data.battery + '%' : 'cât %'}) și dacă funcționează Face ID/TrueTone? Ați putea trimite un screenshot cu IMEI/Serial Number pentru verificare? Mulțumesc!`
                : `Здравствуйте! Подскажите, пожалуйста, какой точный процент АКБ, менялся ли дисплей/детали и работают ли Face ID/TrueTone? Можете ли прислать скриншот "Об этом устройстве" с серийным номером/IMEI? Спасибо!`;

            if (navigator.clipboard) {
                navigator.clipboard.writeText(inquiryText);
            }
            alert('Текст запроса скопирован в буфер обмена!\n\n' + inquiryText);
        });
    }

    function tryInit() {
        if (!isAdvertUrl()) return;
        const data = parseListingDetails();
        injectHUD(data);
    }

    // Запуск при загрузке и периодический опрос для SPA
    tryInit();
    setTimeout(tryInit, 800);
    setTimeout(tryInit, 2000);
    setTimeout(tryInit, 4000);

    let lastUrl = location.href;
    new MutationObserver(() => {
        const url = location.href;
        if (url !== lastUrl) {
            lastUrl = url;
            const existing = document.getElementById('page-agent-999-hud');
            if (existing) existing.remove();
            setTimeout(tryInit, 500);
        }
    }).observe(document, { subtree: true, childList: true });

})();
