import { useOutletContext } from 'react-router-dom'

export type AuthedOutletContext = {
  email: string
  csrfToken: string
}

export function useAuthedOutletContext() {
  return useOutletContext<AuthedOutletContext>()
}
