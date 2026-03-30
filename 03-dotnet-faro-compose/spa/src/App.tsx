import { useState } from 'react'
import './App.css'

const API = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'

type Result = { data: unknown; status: number; duration: number } | null

function App() {
  const [result, setResult] = useState<Result>(null)
  const [loading, setLoading] = useState(false)
  const [fetchError, setFetchError] = useState<string | null>(null)

  const call = async (path: string) => {
    setLoading(true)
    setFetchError(null)
    setResult(null)
    const start = performance.now()
    try {
      const res = await fetch(`${API}${path}`)
      const contentType = res.headers.get('content-type') ?? ''
      const data = contentType.includes('application/json') ? await res.json() : await res.text()
      setResult({ data, status: res.status, duration: Math.round(performance.now() - start) })
    } catch (e) {
      setFetchError(String(e))
    } finally {
      setLoading(false)
    }
  }

  const throwError = () => {
    throw new Error('Simulated frontend error — check Grafana Faro logs')
  }

  return (
    <div className="container">
      <h1>SPA Observability Demo</h1>
      <p>Instrumented with <strong>Grafana Faro</strong>. Every fetch, navigation and error is sent to Grafana.</p>

      <div className="buttons">
        <button onClick={() => call('/weather')}>GET /weather</button>
        <button onClick={() => call('/slow')}>GET /slow</button>
        <button onClick={() => call('/chain')}>GET /chain — distributed trace</button>
        <button onClick={() => call('/chain/slow')}>GET /chain/slow</button>
        <button onClick={() => call('/error')}>GET /error (500)</button>
        <button className="danger" onClick={throwError}>Throw JS Error</button>
      </div>

      {loading && <p className="status">Loading…</p>}
      {fetchError && <pre className="error">{fetchError}</pre>}
      {result && (
        <div className="result">
          <p className="status">HTTP {result.status} — {result.duration}ms</p>
          <pre>{JSON.stringify(result.data, null, 2)}</pre>
        </div>
      )}
    </div>
  )
}

export default App
