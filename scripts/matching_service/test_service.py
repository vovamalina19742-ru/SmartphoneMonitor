import numpy as np
import faiss
from sentence_transformers import SentenceTransformer

def run_test():
    print("Инициализация модели sentence-transformers...")
    model = SentenceTransformer('sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2')

    our_catalog = [
        "iPhone 15 Pro Max",
        "iPhone 15 Pro",
        "Galaxy S24 Ultra",
        "Pixel 8 Pro"
    ]

    competitor_listings = [
        "Айфон 15 про макс 256 гб титан",
        "S24Ultra 512GB Gray",
        "Google Pixel 8 Pro оригинал"
    ]

    print("Кодирование каталога...")
    catalog_embeddings = model.encode(our_catalog).astype('float32')
    faiss.normalize_L2(catalog_embeddings)

    print("Кодирование объявлений конкурентов...")
    comp_embeddings = model.encode(competitor_listings).astype('float32')
    faiss.normalize_L2(comp_embeddings)

    dimension = catalog_embeddings.shape[1]
    index = faiss.IndexFlatIP(dimension)
    index.add(catalog_embeddings)

    similarities, indices = index.search(comp_embeddings, k=1)

    print("\n=== Результаты семантического сопоставления ===")
    for idx, (sim, match_idx) in enumerate(zip(similarities, indices)):
        comp_name = competitor_listings[idx]
        matched_name = our_catalog[match_idx[0]]
        score = float(sim[0])
        status = "Сматчено успешно" if score >= 0.70 else "Недостаточное сходство"
        print(f"Конкурент: '{comp_name}' -> Каталог: '{matched_name}' (Score: {score:.2f}) [{status}]")

    print("\nВсе тесты пройдены успешно!")

if __name__ == "__main__":
    run_test()
