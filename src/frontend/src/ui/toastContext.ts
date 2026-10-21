import { createContext, useContext } from 'react'

export const ToastContext = createContext<(text: string) => void>(() => {})

/** Shows a short confirmation at the bottom of the screen. */
export const useToast = () => useContext(ToastContext)
