import { SubPageHeader } from '../components/SubPageHeader'

/**
 * The in-app data notice — what this app takes off your phone, in the language of the person it is
 * about rather than of a lawyer.
 *
 * It is a SUMMARY and says so. The authoritative text is the published policy at qrlog.az/mexfilik/,
 * and this page links to it rather than restating it: the previous version of this file was written
 * independently and drifted into two claims the published policy contradicts — that check-in photos
 * "are deleted automatically" after a short time, and that only the company's management ever sees
 * them, which quietly omits that the photo is stored at Cloudflare R2 and compared at AWS. A second
 * privacy text is a second thing to keep true, and this one lost.
 *
 * So: everything here is either verifiable in this codebase or a link to the policy of record.
 */
export function PrivacyPage() {
  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Məlumatlarınız" />
      <main className="mx-auto max-w-md space-y-4 p-4 pb-16">
        <Section title="Bu tətbiq nə üçündür">
          İşə gəldiyinizi qeyd etmək üçün. Bu qeyd əməkhaqqınızın hesablanmasının əsasıdır.
        </Section>

        <Section title="Nə toplanır">
          <List
            items={[
              ['📍 Yerləşdiyiniz yer', 'Yalnız skan etdiyiniz an — iş yerində olduğunuzu yoxlamaq üçün. Gün ərzində sizi izləmir, arxa fonda yer məlumatı yığmır.'],
              ['📸 Giriş şəkli', 'Skan anında çəkilən şəkil — girişin sizin tərəfinizdən edildiyini təsdiqləmək üçün.'],
              ['🕒 İş vaxtı', 'Giriş-çıxış saatları, davamiyyət, icazə və məzuniyyət qeydləri, tətbiqi son açma vaxtınız.'],
              ['💼 İş məlumatları', 'Ad, vəzifə, filial, telefon, əməkhaqqı.'],
              ['📱 Cihaz', 'Telefonunuzun brauzer identifikatoru — başqasının sizin yerinizə giriş etməsinin qarşısını almaq üçün.'],
              ['🔑 PIN', 'Yalnız şifrələnmiş formada saxlanılır — açıq mətndə heç yerdə yoxdur.'],
            ]}
          />
        </Section>

        <Section title="Kim görür">
          <p>
            <b>İşlədiyiniz şirkətin</b> rəhbərliyi və filialınıza baxan menecer. Başqa şirkətlər
            sizin məlumatlarınızı görmür.
          </p>
          <p className="mt-2">
            Bundan əlavə, tətbiqin işləməsi üçün bir neçə texniki xidmət məlumatı emal edir — şəkillər{' '}
            <b>Cloudflare R2</b>-də saxlanılır, üz müqayisəsi <b>AWS Rekognition</b> ilə aparılır,
            bildirişlər <b>Web Push</b> ilə göndərilir, «Süni intellekt köməkçisi» çatının cavabları <b>OpenAI</b>{' '}
            vasitəsilə hazırlanır (çata yalnız yazdığınız mesaj və davamiyyət göstəriciləriniz gedir —
            şəkilləriniz getmir). Onlar məlumatı yalnız QRLog adından emal edir.
          </p>
          <p className="mt-2">Məlumatlarınız reklam üçün istifadə olunmur və satılmır.</p>
        </Section>

        <Section title="Şəkil çəkilməsi sizi narahat edirsə">
          Rəhbərinizlə danışın — şəkil tələbi ayrı-ayrı işçilər üçün ləğv edilə bilər. Bu halda
          girişiniz yenə lokasiya və cihaz yoxlaması ilə qeydə alınır.
        </Section>

        <Section title="Hüquqlarınız">
          <p>
            Öz məlumatlarınıza baxmaq, düzəltmək və ya silinməsini tələb etmək hüququnuz var. Əvvəlcə
            iş yerinizə müraciət edin — məlumatların idarəçisi işlədiyiniz şirkətdir.
          </p>
          <p className="mt-2">
            Hesabınızın silinməsi qaydası:{' '}
            <ExternalLink href="https://qrlog.az/hesab-silinmesi/">qrlog.az/hesab-silinmesi</ExternalLink>
          </p>
        </Section>

        <div className="rounded-3xl border border-slate-200 bg-white p-5 text-sm text-slate-600 shadow-sm">
          Bu səhifə qısa izahdır. Tam və rəsmi mətn:{' '}
          <ExternalLink href="https://qrlog.az/mexfilik/">Məxfilik Siyasəti</ExternalLink> — saxlanma
          müddətləri, təhlükəsizlik və əlaqə məlumatları orada yazılıb.
        </div>
      </main>
    </div>
  )
}

/** Opens outside the app: the policy of record lives on the website, not in this bundle. */
function ExternalLink({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className="font-semibold text-blue-600 underline underline-offset-4"
    >
      {children}
    </a>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
      <h2 className="text-base font-bold text-slate-900">{title}</h2>
      <div className="mt-2 text-sm leading-relaxed text-slate-600">{children}</div>
    </section>
  )
}

function List({ items }: { items: [string, string][] }) {
  return (
    <ul className="space-y-2">
      {items.map(([label, body]) => (
        <li key={label}>
          <span className="font-semibold text-slate-800">{label}</span>
          <span className="block">{body}</span>
        </li>
      ))}
    </ul>
  )
}
