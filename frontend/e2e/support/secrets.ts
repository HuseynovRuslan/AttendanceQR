import type { Locator } from '@playwright/test'

// Typing a credential with fill() / type() / keyboard.* puts the value into the step title of the
// HTML report ("Fill \"…\"") — checked, it does. These go through evaluate(), whose title is just
// "Evaluate", and the specs that use them run with tracing off, because a trace records arguments
// and input values regardless. The events are the ones a person's typing and pasting raise, so the
// app's own handlers do the work: nothing is set behind React's back.

/** Puts `value` into a React-controlled <input> as typing would: native setter + input event. */
export async function enterSecret(input: Locator, value: string): Promise<void> {
  await input.evaluate((el: HTMLInputElement, v: string) => {
    el.focus()
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
    setter.call(el, v)
    el.dispatchEvent(new Event('input', { bubbles: true }))
  }, value)
}

/** Pastes a PIN into the first box of PinInput — the path a temporary PIN sent by message takes. */
export async function pasteSecret(input: Locator, value: string): Promise<void> {
  await input.evaluate((el: HTMLInputElement, v: string) => {
    el.focus()
    const data = new DataTransfer()
    data.setData('text', v)
    el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }))
  }, value)
}
