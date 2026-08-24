import uvicorn
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Optional, Tuple
import numpy as np
import faiss
import os
from sentence_transformers import SentenceTransformer

app = FastAPI(title="Smartphone Monitor Matching & Guardrail Service")

# 1. Initialize SentenceTransformer model
# Using a lightweight multilingual model that maps semantic concepts across languages
print("Loading sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2...")
embed_model = SentenceTransformer('sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2')

# 2. Canonical catalog of devices
CANONICAL_DEVICES = [
    # Apple
    "iPhone 15 Pro Max", "iPhone 15 Pro", "iPhone 15 Plus", "iPhone 15",
    "iPhone 14 Pro Max", "iPhone 14 Pro", "iPhone 14 Plus", "iPhone 14",
    "iPhone 13 Pro Max", "iPhone 13 Pro", "iPhone 13 Mini", "iPhone 13",
    "iPhone 12 Pro Max", "iPhone 12 Pro", "iPhone 12 Mini", "iPhone 12",
    "iPhone 11 Pro Max", "iPhone 11 Pro", "iPhone 11",
    "iPhone XS Max", "iPhone XS", "iPhone XR", "iPhone X",
    "iPhone SE 2022", "iPhone SE 2020", "iPhone SE 3", "iPhone SE 2", "iPhone SE",
    "iPhone 8 Plus", "iPhone 8", "iPhone 7 Plus", "iPhone 7",
    # Samsung
    "Galaxy S24 Ultra", "Galaxy S24+", "Galaxy S24",
    "Galaxy S23 Ultra", "Galaxy S23+", "Galaxy S23",
    "Galaxy S22 Ultra", "Galaxy S22+", "Galaxy S22",
    "Galaxy S21 Ultra", "Galaxy S21 FE", "Galaxy S21+", "Galaxy S21",
    "Galaxy S20 FE", "Galaxy S20 Ultra", "Galaxy S20+", "Galaxy S20",
    "Galaxy Note 20 Ultra", "Galaxy Note 20", "Galaxy Note 10+", "Galaxy Note 10", "Galaxy Note 9",
    "Galaxy A54", "Galaxy A34", "Galaxy A24", "Galaxy A14",
    "Galaxy A53", "Galaxy A33", "Galaxy A23", "Galaxy A13",
    "Galaxy A52s", "Galaxy A52", "Galaxy A72", "Galaxy A32", "Galaxy A22", "Galaxy A12",
    "Galaxy A51", "Galaxy A71", "Galaxy A31", "Galaxy A21s", "Galaxy A50", "Galaxy A70",
    "Galaxy A30", "Galaxy A40", "Galaxy A10", "Galaxy A05", "Galaxy A04", "Galaxy A03",
    "Galaxy M12", "Galaxy M21", "Galaxy M31", "Galaxy M51", "Galaxy M32", "Galaxy M52",
    "Galaxy Z Fold", "Galaxy Z Flip",
    # Xiaomi
    "Xiaomi Redmi Note 13 Pro+", "Xiaomi Redmi Note 13 Pro", "Xiaomi Redmi Note 13",
    "Xiaomi Redmi Note 12 Pro", "Xiaomi Redmi Note 12",
    "Xiaomi Redmi Note 11 Pro", "Xiaomi Redmi Note 11",
    "Xiaomi Redmi Note 10 Pro", "Xiaomi Redmi Note 10s", "Xiaomi Redmi Note 10",
    "Xiaomi Redmi Note 9 Pro", "Xiaomi Redmi Note 9s", "Xiaomi Redmi Note 9",
    "Xiaomi Redmi Note 8 Pro", "Xiaomi Redmi Note 8",
    "Xiaomi Redmi 13c", "Xiaomi Redmi 12c", "Xiaomi Redmi 10c", "Xiaomi Redmi 9c",
    "Xiaomi Mi 12", "Xiaomi Mi 10", "Xiaomi Mi 9",
    "Xiaomi Poco X6 Pro", "Xiaomi Poco X6", "Xiaomi Poco X5 Pro", "Xiaomi Poco X5",
    "Xiaomi Poco X4 Pro", "Xiaomi Poco X3 Pro", "Xiaomi Poco X3 NFC", "Xiaomi Poco X3",
    "Xiaomi Poco F5", "Xiaomi Poco F4", "Xiaomi Poco M5s", "Xiaomi Poco M5", "Xiaomi Poco M4",
    # Google
    "Pixel 8 Pro", "Pixel 8a", "Pixel 8",
    "Pixel 7 Pro", "Pixel 7a", "Pixel 7",
    "Pixel 6 Pro", "Pixel 6a", "Pixel 6",
    "Pixel 5a", "Pixel 5", "Pixel 4a", "Pixel 4 XL", "Pixel 4"
]

# 3. Create FAISS Index
print("Encoding canonical database...")
catalog_embeddings = embed_model.encode(CANONICAL_DEVICES).astype('float32')
faiss.normalize_L2(catalog_embeddings)

dimension = catalog_embeddings.shape[1]
faiss_index = faiss.IndexFlatIP(dimension)
faiss_index.add(catalog_embeddings)
print(f"FAISS index loaded with {len(CANONICAL_DEVICES)} devices.")

# 4. Request / Response Schemas
class MatchRequest(BaseModel):
    title: str
    brand: Optional[str] = ""

class MatchResponse(BaseModel):
    original_title: str
    matched_model: str
    score: float
    status: str  # "Succeeded" or "CheckRequired"

class GuardrailRequest(BaseModel):
    current_price: float
    cost_price: float
    market_average: float
    proposed_price: float
    min_markup_percent: Optional[float] = 5.0
    max_price_drop_percent: Optional[float] = 15.0

class GuardrailResponse(BaseModel):
    is_valid: bool
    warning_message: str

@app.post("/match", response_model=MatchResponse)
def match_device(req: MatchRequest):
    # Prepare prompt by attaching brand prefix if available
    query = req.title
    if req.brand and req.brand.lower() not in query.lower():
        query = f"{req.brand} {query}"

    # Embed query
    query_vector = embed_model.encode([query]).astype('float32')
    faiss.normalize_L2(query_vector)

    # Search closest neighbor
    similarities, indices = faiss_index.search(query_vector, k=1)
    
    score = float(similarities[0][0])
    match_idx = int(indices[0][0])
    matched_model = CANONICAL_DEVICES[match_idx]

    # Decide confidence threshold (0.50 is standard for multilingual embeddings in short names)
    CONFIDENCE_THRESHOLD = 0.50
    status = "Succeeded" if score >= CONFIDENCE_THRESHOLD else "CheckRequired"

    return MatchResponse(
        original_title=req.title,
        matched_model=matched_model,
        score=score,
        status=status
    )

@app.post("/validate", response_model=GuardrailResponse)
def validate_price(req: GuardrailRequest):
    # Rule 1: Min profitability
    floor_price = req.cost_price * (1 + req.min_markup_percent / 100.0)
    if req.proposed_price < floor_price:
        return GuardrailResponse(
            is_valid=False,
            warning_message=f"Предложенная цена {req.proposed_price:.2f} MDL ниже лимита рентабельности ({floor_price:.2f} MDL)."
        )

    # Rule 2: Max sudden drop
    max_allowed_drop = req.current_price * (1 - req.max_price_drop_percent / 100.0)
    if req.proposed_price < max_allowed_drop:
        return GuardrailResponse(
            is_valid=False,
            warning_message=f"Резкое снижение цены с {req.current_price:.2f} MDL до {req.proposed_price:.2f} MDL (лимит падения: {max_allowed_drop:.2f} MDL)."
        )

    # Rule 3: Anti-anomaly compared to market average
    if req.proposed_price < req.market_average * 0.60:
        return GuardrailResponse(
            is_valid=False,
            warning_message=f"Цена {req.proposed_price:.2f} MDL более чем на 40% ниже средней рыночной ({req.market_average:.2f} MDL) - возможна ошибка парсинга или фейк."
        )

    return GuardrailResponse(
        is_valid=True,
        warning_message="Валидация успешно пройдена."
    )

class ArbitrageDemandItem(BaseModel):
    id: str
    title: str
    budget_price: float
    brand: Optional[str] = ""
    model: Optional[str] = ""
    storage_gb: Optional[int] = 0

class ArbitrageSupplyItem(BaseModel):
    id: str
    title: str
    price: float
    url: str
    brand: Optional[str] = ""
    model: Optional[str] = ""
    storage_gb: Optional[int] = 0

class ArbitrageRequest(BaseModel):
    demands: List[ArbitrageDemandItem]
    supplies: List[ArbitrageSupplyItem]
    min_profit_margin: Optional[float] = 200.0

class ArbitrageMatchDeal(BaseModel):
    demand_id: str
    supply_id: str
    potential_profit: float
    match_score: float

class ArbitrageResponse(BaseModel):
    deals: List[ArbitrageMatchDeal]

@app.post("/match_arbitrage", response_model=ArbitrageResponse)
def match_arbitrage(req: ArbitrageRequest):
    deals = []
    if not req.demands or not req.supplies:
        return ArbitrageResponse(deals=[])

    demand_texts = [f"{d.brand} {d.title}" for d in req.demands]
    supply_texts = [f"{s.brand} {s.title}" for s in req.supplies]

    d_embeddings = embed_model.encode(demand_texts).astype('float32')
    s_embeddings = embed_model.encode(supply_texts).astype('float32')

    faiss.normalize_L2(d_embeddings)
    faiss.normalize_L2(s_embeddings)

    sim_matrix = np.dot(d_embeddings, s_embeddings.T)

    for i, demand in enumerate(req.demands):
        for j, supply in enumerate(req.supplies):
            profit = demand.budget_price - supply.price
            if profit >= req.min_profit_margin:
                score = float(sim_matrix[i, j])
                if score >= 0.50:
                    deals.append(ArbitrageMatchDeal(
                        demand_id=demand.id,
                        supply_id=supply.id,
                        potential_profit=profit,
                        match_score=score
                    ))

    return ArbitrageResponse(deals=deals)

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8000)
