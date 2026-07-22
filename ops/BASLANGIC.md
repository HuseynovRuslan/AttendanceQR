# QRLog — layihəyə necə qoşulmalı

Bu sənəd kod yazmağı bilməyən, amma Claude Code ilə işləmək istəyən komanda üzvü üçündür.

## Nə edə bilərsən, nə edə bilməzsən

| ✅ Sənin sahən | ❌ Toxunmadığın yer |
|---|---|
| `staging` budağı | `main` budağı |
| test.qrlog.az | bax.qrlog.az / cleanfix.qrlog.az / ecaf.qrlog.az |
| Test məlumatları | 114 real işçinin davamiyyəti |

Serverə girişin yoxdur və olmamalıdır. Sənə lazım deyil — kodu göndərirsən, server özü götürüb yayımlayır.

## Bir dəfəlik quraşdırma

1. **Git** — https://git-scm.com/downloads
2. **Node.js** (LTS) — https://nodejs.org
3. **Docker Desktop** — https://docker.com/products/docker-desktop
4. **Claude Code** — https://claude.com/claude-code
5. GitHub hesabı aç və Ruslandan repoya dəvət istə

Sonra kodu götür:

    git clone https://github.com/HuseynovRuslan/AttendanceQR.git
    cd AttendanceQR
    git checkout staging

## Gündəlik iş

Claude Code-u layihə qovluğunda aç:

    claude

Nə istədiyini adi dillə yaz. Məsələn:

> *«Admin paneldə işçilər siyahısına filial üzrə süzgəc əlavə et»*

Claude kodu özü yazacaq. Sənin işin — nə istədiyini aydın izah etmək və nəticəni yoxlamaq.

### Nəticəni öz kompüterində gör

    docker compose up --build

Sonra brauzerdə: **http://localhost:8081**

Bu, sənin öz kompüterindədir — heç kimə təsir etmir, istədiyini sındıra bilərsən.

### Başqalarına göstərmək üçün

    git add -A
    git commit -m "nə etdiyini bir cümlə ilə yaz"
    git push origin staging

**2 dəqiqə sonra** dəyişikliyin burada olacaq: **https://test.qrlog.az**

Telefonda da aça bilərsən — real cihazda necə göründüyünü görmək üçün ən yaxşı yol budur.

Test şirkətinə giriş: telefon `+994500000000`, PIN `1234`.

## Bilməli olduğun qaydalar

**Hər şey azərbaycancadır.** İstifadəçinin gördüyü hər söz — düymələr, xəbərdarlıqlar, hesabatlar.

**`main` budağına push etmə.** Orada 114 nəfərin real davamiyyəti dayanır. Sənin işin `staging`-dədir.

**Səhv etməkdən qorxma.** test.qrlog.az məhz bunun üçündür — orada sınmış bir şey heç kimə zərər vermir. Qorxulu olan, sınıq kodu `main`-ə göndərməkdir.

**Claude Code-a serverin parolunu vermə.** Heç kim verməməlidir.

## İlişəndə

`CLAUDE.md` faylını oxu — layihənin qaydaları orada yazılıb, Claude Code onu avtomatik oxuyur.

Nəsə alınmırsa, Claude-a səhvin **tam mətnini** göstər. Ekran şəklindən çox, mətn kömək edir.
