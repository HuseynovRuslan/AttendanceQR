import { SubPageHeader } from '../components/SubPageHeader'

/**
 * The plain-language data-processing notice.
 *
 * Written to be read by the person it is about — a groundskeeper on a phone, not a lawyer — because a
 * notice nobody can read has not informed anyone. It is deliberately specific about the three things
 * that worry people (is it tracking me all day, who sees my photo, what happens to my salary data)
 * rather than hedging in the abstract.
 *
 * This is the working text, not a substitute for a reviewed policy: a lawyer should check it before
 * the product is sold to a company that asks for one.
 */
export function PrivacyPage() {
  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Məxfilik bildirişi" />
      <main className="mx-auto max-w-md space-y-4 p-4 pb-16">
        <Section title="Nə üçün bu tətbiq var">
          İşə gəldiyinizi qeyd etmək üçün. Bu qeyd əməkhaqqınızın hesablanmasının əsasıdır.
        </Section>

        <Section title="Hansı məlumatlar toplanır">
          <List
            items={[
              ['📍 Yerləşdiyiniz yer', 'Yalnız QR kodu skan etdiyiniz anda. Gün ərzində sizi izləmir, arxa fonda yer məlumatı yığmır.'],
              ['📸 Giriş şəkli', 'Skan zamanı ön kamera ilə çəkilən şəkil — girişin sizin tərəfinizdən edildiyini təsdiqləmək üçün.'],
              ['🕒 İş vaxtı', 'Giriş və çıxış saatları, davamiyyət, icazə və məzuniyyət qeydləri.'],
              ['💼 İş məlumatları', 'Ad, vəzifə, filial, telefon, əməkhaqqı (yalnız şirkət rəhbərliyi görür).'],
              ['📱 Cihaz', 'Telefonunuzun brauzer identifikatoru — başqasının sizin yerinizə giriş etməsinin qarşısını almaq üçün.'],
            ]}
          />
        </Section>

        <Section title="Kim görür">
          Yalnız <b>işlədiyiniz şirkətin</b> rəhbərliyi və menecerləriniz. Məlumatlarınız başqa
          şirkətlərə göstərilmir, üçüncü tərəfə satılmır və reklam üçün istifadə olunmur.
        </Section>

        <Section title="Nə qədər saxlanılır">
          Davamiyyət qeydləri əmək qanunvericiliyinin tələb etdiyi müddətdə saxlanılır. Giriş
          şəkilləri daha qısa müddət saxlanılır və avtomatik silinir.
        </Section>

        <Section title="Hüquqlarınız">
          <List
            items={[
              ['Görmək', 'Sizin haqqınızda saxlanılan məlumatları tələb edə bilərsiniz.'],
              ['Düzəltmək', 'Səhv məlumatın düzəldilməsini tələb edə bilərsiniz.'],
              ['Soruşmaq', 'Məlumatın necə istifadə olunduğunu soruşa bilərsiniz.'],
            ]}
          />
          <p className="mt-2">Bunun üçün rəhbərinizlə və ya şirkətin kadrlar bölməsi ilə əlaqə saxlayın.</p>
        </Section>

        <Section title="Şəkil çəkilməsi sizi narahat edirsə">
          Rəhbərinizlə danışın — şəkil tələbi ayrı-ayrı işçilər üçün ləğv edilə bilər. Bu halda
          giriş yenə lokasiya və cihaz yoxlaması ilə qeydə alınır.
        </Section>

        <p className="pt-2 text-center text-xs text-slate-400">
          Bu mətn sadə dildə izahdır. Şirkətinizin rəsmi məlumat siyasəti ilə birlikdə oxunmalıdır.
        </p>
      </main>
    </div>
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
