import { IconAlert } from './icons'

/**
 * Sahənin altındakı qırmızı xəta mətni ("Nömrə 10 rəqəm olmalıdır" kimi).
 * Harada: PhoneInput, IdentifierInput, LoginPage (PIN), RegisterPage.
 * Props: id — input-un aria-describedby-si bu id-yə bağlanır ki, ekran oxuyucu xətanı sahə ilə birlikdə
 * oxusun; message — xəta yoxdursa (null) heç nə göstərilmir.
 *
 * Component — öz HTML-ini qaytaran funksiyadır; props isə ona çöldən verilən parametrlərdir.
 */
export function FieldError({ id, message }: { id: string; message?: string | null }) {
  if (!message) return null
  return (
    <div id={id} className="field-err" role="alert">
      {/* İkon yalnız gözə görə; mətn özü kifayətdir, ona görə ekran oxuyucudan gizlədilir. */}
      <IconAlert aria-hidden="true" />
      <span>{message}</span>
    </div>
  )
}
