import type { ReactNode } from 'react'
import { isNativeApp, LANDING_URL } from '../../lib/host'
import { IconArrowLeft } from '../icons'
import { AuthScene, type AuthSceneVariant } from './AuthScene'
import { riseOrder } from './riseOrder'
import './AuthLayout.css'

type AuthLayoutProps = {
  variant: AuthSceneVariant
  sceneAlertKey?: number
  children: ReactNode
}

export function AuthLayout({ variant, sceneAlertKey = 0, children }: AuthLayoutProps) {
  const nativeApp = isNativeApp()

  return (
    <div className={nativeApp ? 'auth-layout' : 'auth-layout auth-layout--scene'}>
      {/* Form column */}
      <main className="auth-form-col auth-in-left">
        <div className="auth-topbar auth-rise" style={riseOrder(0)}>
          {nativeApp ? (
            <img className="auth-logo" src="/brand/qrlog-logo.png" alt="QRLog" />
          ) : (
            <>
              <a className="auth-logo-link" href={LANDING_URL} aria-label="QRLog — ana səhifə">
                <img className="auth-logo" src="/brand/qrlog-logo.png" alt="" />
              </a>
              <a className="auth-home" href={LANDING_URL}>
                <IconArrowLeft aria-hidden="true" />
                Ana səhifə
              </a>
            </>
          )}
        </div>
        <div className="auth-body">
          <div className="auth-content">{children}</div>
        </div>
      </main>

      {/* Scene column */}
      {!nativeApp && (
        <div className="auth-scene-col">
          <AuthScene variant={variant} alertKey={sceneAlertKey} className="auth-in-right" />
        </div>
      )}
    </div>
  )
}
