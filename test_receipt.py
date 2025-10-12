#!/usr/bin/env python3
"""
Script per generare un'immagine di test di uno scontrino per i test E2E
"""
from PIL import Image, ImageDraw, ImageFont
import io
import base64

def create_test_receipt_image():
    # Crea un'immagine bianca
    width, height = 400, 600
    image = Image.new('RGB', (width, height), 'white')
    draw = ImageDraw.Draw(image)
    
    # Tenta di usare un font di sistema, altrimenti usa quello default
    try:
        font = ImageFont.truetype("arial.ttf", 16)
        title_font = ImageFont.truetype("arial.ttf", 20)
    except:
        font = ImageFont.load_default()
        title_font = ImageFont.load_default()
    
    # Contenuto dello scontrino
    lines = [
        "CARREFOUR EXPRESS",
        "Via Roma 123, Milano",
        "P.IVA: 12345678901",
        "",
        "SCONTRINO FISCALE",
        "12/10/2025 15:30:25",
        "",
        "LATTE INTERO LT 1.0    €2.50",
        "PANE INTEGRALE 500G    €1.80",
        "POMODORI KG 1.200      €3.60",
        "PASTA BARILLA 500G     €1.20",
        "",
        "SUBTOTALE:             €9.10",
        "TOTALE:                €9.10",
        "",
        "CONTANTI:              €10.00",
        "RESTO:                 €0.90",
        "",
        "Grazie per la visita!"
    ]
    
    # Disegna le linee
    y = 30
    for line in lines:
        if "CARREFOUR" in line:
            draw.text((20, y), line, fill='black', font=title_font)
        else:
            draw.text((20, y), line, fill='black', font=font)
        y += 25
    
    # Salva in memoria come bytes
    img_buffer = io.BytesIO()
    image.save(img_buffer, format='JPEG', quality=95)
    img_buffer.seek(0)
    
    return img_buffer.getvalue()

def save_test_image():
    """Salva l'immagine di test su file"""
    img_data = create_test_receipt_image()
    with open('test_receipt.jpg', 'wb') as f:
        f.write(img_data)
    print("✅ Immagine di test creata: test_receipt.jpg")
    
    # Crea anche la versione base64 per API calls
    img_b64 = base64.b64encode(img_data).decode('utf-8')
    with open('test_receipt_b64.txt', 'w') as f:
        f.write(img_b64)
    print("✅ Versione base64 creata: test_receipt_b64.txt")

if __name__ == "__main__":
    save_test_image()