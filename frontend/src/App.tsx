import { Navigate, Route, Routes } from 'react-router-dom'
import { RequireAuth, RequirePerm } from './auth/RequireAuth'
import { useAuth } from './auth/AuthProvider'
import { useLocale } from './i18n/LocaleProvider'
import { AppLayout } from './components/AppLayout'
import { CatalogPage } from './pages/CatalogPage'
import { LookupsPage } from './pages/LookupsPage'
import { DesignerPage } from './pages/DesignerPage'
import { EmbedReportPage } from './pages/EmbedReportPage'
import { SsoPage } from './pages/SsoPage'
import { ActivityPage } from './pages/ActivityPage'
import { InsightsPage } from './pages/InsightsPage'
import { DeliveryPage } from './pages/DeliveryPage'
import { AnalyticsListPage } from './pages/AnalyticsListPage'
import { AnalyticsDesignerPage } from './pages/AnalyticsDesignerPage'
import { AnalyticsDashboardPage } from './pages/AnalyticsDashboardPage'
import { PermissionsPage } from './pages/PermissionsPage'
import { ReportListPage } from './pages/ReportListPage'
import { ReportViewPage } from './pages/ReportViewPage'
import { RolesPage } from './pages/RolesPage'
import { SettingsPage } from './pages/SettingsPage'
import { UsersPage } from './pages/UsersPage'
import { LoginPage } from './pages/LoginPage'
import { ResetPasswordPage } from './pages/ResetPasswordPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/sso" element={<SsoPage />} />
      <Route path="/embed/reports/:id" element={<EmbedReportPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppLayout />}>
          <Route path="/" element={<HomePage />} />
          <Route
            path="/delivery"
            element={
              <RequirePerm permission="reports.delivery">
                <DeliveryPage />
              </RequirePerm>
            }
          />
          <Route path="/catalog/delivery" element={<Navigate to="/delivery" replace />} />
          <Route
            path="/catalog"
            element={
              <RequirePerm permission="datasets.view">
                <CatalogPage />
              </RequirePerm>
            }
          />
          <Route
            path="/catalog/lookups"
            element={
              <RequirePerm permission="lookups.view">
                <LookupsPage />
              </RequirePerm>
            }
          />
          <Route
            path="/reports/new"
            element={
              <RequirePerm permission="reports.create">
                <DesignerPage />
              </RequirePerm>
            }
          />
          <Route
            path="/reports/:id"
            element={
              <RequirePerm permission="reports.update">
                <DesignerPage />
              </RequirePerm>
            }
          />
          <Route
            path="/reports/:id/view"
            element={
              <RequirePerm permission="reports.view">
                <ReportViewPage />
              </RequirePerm>
            }
          />
          <Route
            path="/users"
            element={
              <RequirePerm permission="users.view">
                <UsersPage />
              </RequirePerm>
            }
          />
          <Route
            path="/roles"
            element={
              <RequirePerm permission="roles.view">
                <RolesPage />
              </RequirePerm>
            }
          />
          <Route
            path="/permissions"
            element={
              <RequirePerm permission="permissions.view">
                <PermissionsPage />
              </RequirePerm>
            }
          />
          <Route
            path="/activity"
            element={
              <RequirePerm permission="logs.view">
                <ActivityPage />
              </RequirePerm>
            }
          />
          <Route
            path="/insights"
            element={
              <RequirePerm permission="insights.view">
                <InsightsPage />
              </RequirePerm>
            }
          />
          <Route
            path="/analytics"
            element={
              <RequirePerm permission="analytics.view">
                <AnalyticsListPage />
              </RequirePerm>
            }
          />
          <Route
            path="/analytics/new"
            element={
              <RequirePerm permission="analytics.create">
                <AnalyticsDesignerPage />
              </RequirePerm>
            }
          />
          <Route
            path="/analytics/:id/view"
            element={
              <RequirePerm permission="analytics.view">
                <AnalyticsDashboardPage />
              </RequirePerm>
            }
          />
          <Route
            path="/analytics/:id"
            element={
              <RequirePerm permission="analytics.update">
                <AnalyticsDesignerPage />
              </RequirePerm>
            }
          />
          <Route
            path="/settings"
            element={
              <RequirePerm permission="settings.view">
                <SettingsPage />
              </RequirePerm>
            }
          />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Route>
    </Routes>
  )
}

function HomePage() {
  const { can } = useAuth()
  const { t } = useLocale()
  if (can('reports.view')) {
    return <ReportListPage />
  }
  if (can('datasets.view')) {
    return <Navigate to="/catalog" replace />
  }
  if (can('lookups.view')) {
    return <Navigate to="/catalog/lookups" replace />
  }
  if (can('users.view')) {
    return <Navigate to="/users" replace />
  }
  if (can('roles.view')) {
    return <Navigate to="/roles" replace />
  }
  if (can('permissions.view')) {
    return <Navigate to="/permissions" replace />
  }
  if (can('logs.view')) {
    return <Navigate to="/activity" replace />
  }
  if (can('insights.view')) {
    return <Navigate to="/insights" replace />
  }
  if (can('analytics.view')) {
    return <Navigate to="/analytics" replace />
  }
  if (can('settings.view')) {
    return <Navigate to="/settings" replace />
  }
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <div className="kicker">{t('accessKicker')}</div>
          <h1>{t('noPagesTitle')}</h1>
          <p className="lede">{t('noPagesLede')}</p>
        </div>
      </header>
    </section>
  )
}
