# AttendanceQR — Frontend

Vite + React + TypeScript + Tailwind CSS. İşçi skan ekranı (bu mərhələ). Kiosk və admin panel
sonrakı mərhələlərdə.

## Quraşdırma

```bash
cd frontend
npm install
```

`.env.example` faylını `.env`-ə kopyalayın və lazım olsa API ünvanını dəyişin:

```
VITE_API_URL=http://localhost:5103
```

## İşə salma

```bash
npm run dev      # http://localhost:5173
npm run build    # tip yoxlaması (tsc) + prod build → dist/
npm run preview  # build-i lokal önizləmə
```

Backend eyni anda işləməlidir (kök qovluqdan):

```bash
# PostgreSQL işləyir olsun, sonra:
cd src/AttendanceQR.Api
dotnet run    # http://localhost:5103
```

> Backend-də CORS açıqdır (dev) — frontend `:5173`-dən `:5103`-ə sərbəst sorğu göndərir.

## Ekranlar / route-lar

| Route | Nə edir |
|-------|---------|
| `/login` | Email + parol → JWT |
| `/activate?token=…` | Dəvət linki: parol təyin + cihaz bağlama → JWT |
| `/scan` | **Əsas ekran** — kamera ilə QR skan, GPS + cihaz göndərir, nəticəni göstərir |
| `/kiosk`, `/admin` | Placeholder (sonrakı mərhələlər) |

## ⚠ Kamera və GPS — HTTPS tələbi

Brauzer `getUserMedia` (kamera) və `geolocation` (GPS) API-lərini **yalnız təhlükəsiz
kontekstdə** işə salır:

- **`localhost`** təhlükəsiz sayılır → kompüterdə `http://localhost:5173` kamera/GPS ilə işləyir
  (Chrome-da icazə pəncərəsi çıxacaq).
- **Real telefon** (məs. `http://192.168.x.x:5173`) təhlükəsiz DEYİL → kamera açılmır.
  Telefonda test üçün seçimlər:
  - Frontend-i HTTPS ilə deploy et (Netlify/Vercel/Cloudflare Pages) və backend-i də HTTPS-də ver;
  - və ya `ngrok http 5173` kimi tunel ilə müvəqqəti HTTPS ünvan al;
  - və ya lokal şəbəkədə `mkcert` ilə etibarlı sertifikat qur.

## Lokalda kamera olmadan test

Kompüterdə real kamera yoxdursa, `/scan`-da QR-ı fiziki göstərmək çətindir. Test üçün:

1. Backend-dən QR token al: `GET /api/dev/qr/{locationId}` (dev) və ya `GET /api/kiosk/token/{locationId}`.
2. Həmin token-i onlayn "QR code generator"-a yapışdırıb şəkil yarat, ekranda aç.
3. `/scan` ekranını noutbukun kamerasına həmin QR-a tut.

GPS: Chrome DevTools → **Sensors** panelində məkanı (məs. Bakı 40.4093, 49.8671) manual təyin
edə bilərsiniz ki, radius yoxlaması keçsin.

## Cihaz fingerprint

`src/lib/device.ts` ilk açılışda `crypto.randomUUID()` yaradıb `localStorage`-də saxlayır. Bu, backend-in
`DeviceFingerprint`-idir — aktivasiya cihazı bağlayır, hər skan bu dəyəri göndərir. localStorage
təmizlənərsə yeni fingerprint yaranır və cihaz artıq bağlı olmaz (yenidən cihaz dəyişimi lazımdır).
