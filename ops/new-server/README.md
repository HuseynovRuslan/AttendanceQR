# Yeni host (VPS L) — hazırlıq və mərhələli köçürmə

Köhnə serverdə (62.84.179.39) HEÇ NƏ dəyişmir, DNS-ə toxunulmur — yeni host əvvəlcə staging-lə
sınanır, sonra yük testi, yalnız bundan sonra production cutover planlaşdırılır (ayrı qərar).

## 0. VPS yaradılanda (provayder panelində — əl işi)
- Ubuntu 24.04 LTS, VPS L.
- **SSH açarı** kimi `ops` açarının public hissəsini əlavə edin (parol girişi heç vaxt açılmır).
- İlk giriş: `ssh -i ~/.ssh/qrlog_vps_l root@<YENİ_IP>` — yalnız bootstrap üçün; sonra root bağlanır.

## 1. Bootstrap (bir dəfə, root kimi)
    scp -i ~/.ssh/qrlog_vps_l ops/new-server/bootstrap.sh root@<YENİ_IP>:/root/
    ssh -i ~/.ssh/qrlog_vps_l root@<YENİ_IP> 'bash /root/bootstrap.sh phase1 "<public key>"'
    # AYRI terminalda açarla girişi sına:  ssh -i ~/.ssh/qrlog_vps_l deploy@<YENİ_IP> 'echo ok && sudo -n true'
    # yalnız uğurdan SONRA (köhnə sessiya açıq qalsın):  sudo bash /root/bootstrap.sh phase2
Nəticə: `deploy` istifadəçisi (sudo), yalnız açarla SSH, root login bağlı, ufw 22/80/443, fail2ban,
avtomatik təhlükəsizlik yeniləmələri, Docker (rəsmi repo) + compose, 4G swap, log limitləri, Asia/Baku.
Yoxlama: `ssh -i ~/.ssh/qrlog_vps_l deploy@<YENİ_IP> 'docker ps && sudo ufw status'`.

## 2. Staging-in köçürülməsi (DNS-siz)
    ssh deploy@<YENİ_IP>
    sudo mkdir -p /opt/qrlog-staging && sudo chown deploy /opt/qrlog-staging
    git clone --branch staging https://github.com/<org>/AttendanceQR.git /opt/qrlog-staging
    cd /opt/qrlog-staging
- `.env.staging` köhnə serverdən **əl ilə** köçürülür (`scp`), repo-ya heç vaxt düşmür. Bu fürsətdə
  staging sirlərini YENİ dəyərlərlə yazmaq olar (staging prod-la heç nə bölüşmür).
- Caddy: `ops/new-server/Caddyfile.staging-internal` → `Caddyfile` kimi (tls internal — LE sertifikatı
  DNS dəyişməmiş alınmır). Compose-a staging üçün ayrıca caddy servisi lazımdır: köhnə serverdə Caddy
  prod stack-in içindədir; yeni hostda staging-i sınayarkən `docker-compose.staging.yml`-ə müvəqqəti
  `caddy` servisi əlavə edilir (prod compose-dakı blokun kopyası, Caddyfile yolu yuxarıdakı fayl).
- Qaldırma (limitlərlə):
      docker compose -f docker-compose.staging.yml -f docker-compose.limits.staging.yml --env-file .env.staging up -d --build
- Staging DB: boş başlayır (miqrasiyalar startup-da). Yük-test tenantı lazımdırsa `/root/lt/seed.sql`
  köhnə serverdən köçürülüb tətbiq edilir.

## 3. Yoxlama (DNS-siz)
- Lokal maşında hosts faylına: `<YENİ_IP> test.qrlog.az api-test.qrlog.az` (müvəqqəti!).
- Caddy-nin daxili CA-sını bir dəfə etibarlı et və ya `curl -k`.
- Smoke: login, skan ekranı, admin dashboard; sonra `loadtest.py` ilə 2000-lik məşq (`--base http://<stg-backend-ip>:8080`).
- Limitlərin işlədiyini gör: `docker stats` — backend 1 GiB tavanını keçmir.

## 4. Sonra (ayrı qərarlar, bu runbook-dan kənar)
- Production cutover planı: DB dump/restore məşqi, R2 dəyişmir, DNS TTL-i əvvəlcədən 5 dəq-ə endirmək,
  cutover yalnız 19:30-dan sonra, köhnə server 7 gün «isti ehtiyat».
- Sir rotasiyası cutover-lə birləşdirilir (JWT/DB/R2 dərhal; QR sirri poster çapı ilə).
