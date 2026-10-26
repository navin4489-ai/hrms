import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../providers/organizations_provider.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/confirm_dialog.dart';
import '../../widgets/section_card.dart';

/// "More" hub: profile, directory, payroll documents, expenses, assets,
/// performance, helpdesk, announcements, notifications and settings.
class MoreScreen extends ConsumerWidget {
  const MoreScreen({super.key});

  Future<void> _signOut(BuildContext context, WidgetRef ref) async {
    final confirmed = await showConfirmDialog(
      context,
      title: Strings.logoutTitle,
      message: Strings.logoutMessage,
      confirmLabel: Strings.signOut,
      destructive: true,
    );
    if (!confirmed || !context.mounted) return;
    await ref.read(authControllerProvider.notifier).logout();
    if (context.mounted) context.go('/login');
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authStateProvider);
    final user = auth.user;
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final canManageEmployees = auth.hasAnyPermission([
      'employee.view',
      'employee.create',
      'employee.edit',
      'employee.manage',
    ]);
    final canCreateEmployee = auth.hasPermission('employee.create');
    final canManageUsers = auth.hasPermission('admin.users');
    final canManageSystem = auth.hasAnyPermission([
      'admin.roles',
      'admin.config',
      'audit.view',
    ]);
    final canViewReports = auth.hasPermission('report.view');
    final canManagePayroll = auth.hasPermission('payroll.manage');
    final canManagePerformance = auth.hasPermission('performance.manage');
    final canManageTraining = auth.hasPermission('training.manage');
    final canCreateAnnouncement = auth.hasPermission('announcement.create');
    final canManageDocuments =
        auth.hasPermission('document.manage') &&
        auth.hasPermission('employee.view');
    final canManageAssets =
        auth.hasPermission('asset.manage') &&
        auth.hasPermission('employee.view');
    final canManageLifecycle = auth.hasAnyPermission([
      'employee.create',
      'employee.manage',
    ]);
    final canManageAttendance = auth.hasPermission('attendance.manage');
    final canManageCases = auth.hasPermission('case.manage');
    final canManageOrgs = auth.hasPermission('admin.organizations');
    final canUseChat = auth.hasPermission('chat.use');
    final canCall = auth.hasPermission('call.use');

    // Per-org module flags: a module absent from the set is hidden for this org.
    // While loading (or on error) we default to showing everything so admins are
    // never locked out by a transient fetch failure.
    final modules = ref.watch(meModulesProvider).value;
    bool moduleOn(String m) => modules == null || modules.contains(m);
    final payrollModuleOn = modules?.contains('payroll') ?? false;

    return AppScaffold(
      title: Strings.moreTitle,
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(meProvider);
          ref.invalidate(meModulesProvider);
        },
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            // -------------------------------------------------- account card
            Card(
              clipBehavior: Clip.antiAlias,
              child: ListTile(
                leading: CircleAvatar(
                  backgroundColor: scheme.primaryContainer,
                  foregroundColor: scheme.onPrimaryContainer,
                  child: Text(
                    user?.initials ?? '?',
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                title: Text(user?.displayName ?? Strings.profile),
                subtitle: Text(
                  [
                    if (user != null) ...[
                      if (user.designation != null) user.designation!,
                      if (user.department != null) user.department!,
                      user.employeeNumber,
                    ],
                  ].join(' · '),
                ),
                trailing: const Icon(Icons.chevron_right_rounded),
                onTap: () => context.go('/more/profile'),
              ),
            ),
            const SizedBox(height: 12),
            if (canUseChat && moduleOn('chat')) ...[
              Card(
                clipBehavior: Clip.antiAlias,
                child: ListTile(
                  leading: CircleAvatar(
                    backgroundColor: scheme.tertiaryContainer,
                    foregroundColor: scheme.onTertiaryContainer,
                    child: const Icon(Icons.forum_rounded),
                  ),
                  title: const Text('Chat & Calls'),
                  subtitle: Text(
                    canCall && moduleOn('calling')
                        ? 'Message, share files and call colleagues'
                        : 'Message and share files with colleagues',
                  ),
                  trailing: const Icon(Icons.chevron_right_rounded),
                  onTap: () => context.push('/chat'),
                ),
              ),
              const SizedBox(height: 12),
            ],
            SectionCard(
              title: Strings.profile,
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
              child: Column(
                children: [
                  _Tile(
                    icon: Icons.person_outline_rounded,
                    label: Strings.profile,
                    onTap: () => context.go('/more/profile'),
                  ),
                  _Tile(
                    icon: Icons.badge_outlined,
                    label: Strings.directory,
                    onTap: () => context.go('/more/directory'),
                  ),
                  _Tile(
                    icon: Icons.history_edu_rounded,
                    label: Strings.employmentHistory,
                    onTap: () => context.go('/more/profile/history'),
                  ),
                  _Tile(
                    icon: Icons.devices_outlined,
                    label: Strings.sessions,
                    onTap: () => context.go('/more/profile/sessions'),
                  ),
                ],
              ),
            ),
            if (canManageEmployees ||
                canManageUsers ||
                canManageSystem ||
                canViewReports ||
                canManagePayroll ||
                canManagePerformance ||
                canManageTraining ||
                canCreateAnnouncement ||
                canManageDocuments ||
                canManageAssets ||
                canManageLifecycle ||
                canManageAttendance ||
                canManageCases ||
                canManageOrgs) ...[
              const SizedBox(height: 12),
              SectionCard(
                title: Strings.adminTools,
                padding: const EdgeInsets.symmetric(
                  horizontal: 8,
                  vertical: 12,
                ),
                child: Column(
                  children: [
                    if (canManageOrgs)
                      _Tile(
                        icon: Icons.apartment_outlined,
                        label: 'Organizations (tenants)',
                        onTap: () => context.go('/more/admin/organizations'),
                      ),
                    if (canManageEmployees)
                      _Tile(
                        icon: Icons.manage_accounts_outlined,
                        label: Strings.manageEmployees,
                        onTap: () => context.go('/more/directory'),
                      ),
                    if (canCreateEmployee)
                      _Tile(
                        icon: Icons.person_add_alt_1_rounded,
                        label: Strings.addEmployee,
                        onTap: () => context.go('/more/directory/new'),
                      ),
                    if (canCreateEmployee)
                      _Tile(
                        icon: Icons.upload_file_outlined,
                        label: 'Import employees from CSV',
                        onTap: () => context.go('/more/admin/employee-import'),
                      ),
                    if (canManageUsers)
                      _Tile(
                        icon: Icons.manage_accounts_outlined,
                        label: 'User accounts',
                        onTap: () => context.go('/more/admin/users'),
                      ),
                    if (canManageSystem)
                      _Tile(
                        icon: Icons.settings_suggest_outlined,
                        label: 'System administration',
                        onTap: () => context.go('/more/admin/system'),
                      ),
                    if (auth.hasPermission('admin.config') &&
                        auth.hasPermission('employee.view'))
                      _Tile(
                        icon: Icons.account_tree_outlined,
                        label: 'Organization setup',
                        onTap: () => context.go('/more/admin/organization'),
                      ),
                    if (auth.hasPermission('admin.config'))
                      _Tile(
                        icon: Icons.alt_route_rounded,
                        label: 'Approval workflows',
                        onTap: () => context.go('/more/admin/workflows'),
                      ),
                    if (auth.hasPermission('admin.config') && moduleOn('leave'))
                      _Tile(
                        icon: Icons.event_available_outlined,
                        label: 'Leave administration',
                        onTap: () => context.go('/more/admin/leave'),
                      ),
                    if (auth.hasPermission('admin.config') &&
                        moduleOn('attendance'))
                      _Tile(
                        icon: Icons.schedule_outlined,
                        label: 'Shift configuration',
                        onTap: () => context.go('/more/admin/shifts'),
                      ),
                    if (canViewReports)
                      _Tile(
                        icon: Icons.query_stats_rounded,
                        label: 'Workforce reports',
                        onTap: () => context.go('/more/admin/reports'),
                      ),
                    if (canManagePayroll && payrollModuleOn)
                      _Tile(
                        icon: Icons.payments_outlined,
                        label: 'Payroll periods',
                        onTap: () => context.go('/more/admin/payroll-periods'),
                      ),
                    if (canManagePayroll && payrollModuleOn)
                      _Tile(
                        icon: Icons.account_balance_outlined,
                        label: 'Salary & tax',
                        onTap: () =>
                            context.go('/more/admin/payroll/salaries'),
                      ),
                    if ((canManagePerformance && moduleOn('performance')) ||
                        (canManageTraining && moduleOn('training')))
                      _Tile(
                        icon: Icons.school_outlined,
                        label: 'Performance & learning',
                        onTap: () => context.go('/more/admin/performance'),
                      ),
                    if (canCreateAnnouncement && moduleOn('announcements'))
                      _Tile(
                        icon: Icons.campaign_outlined,
                        label: 'Create announcement',
                        onTap: () => context.go('/home/announcements/new'),
                      ),
                    if (canManageDocuments && moduleOn('documents'))
                      _Tile(
                        icon: Icons.verified_user_outlined,
                        label: 'Document verification',
                        onTap: () => context.go('/more/admin/documents'),
                      ),
                    if (canManageAssets && moduleOn('assets'))
                      _Tile(
                        icon: Icons.inventory_2_outlined,
                        label: 'Asset inventory',
                        onTap: () => context.go('/more/admin/assets'),
                      ),
                    if (canManageLifecycle &&
                        auth.hasPermission('employee.view'))
                      _Tile(
                        icon: Icons.checklist_rounded,
                        label: 'Onboarding & offboarding',
                        onTap: () => context.go('/more/admin/lifecycle'),
                      ),
                    if (canManageAttendance && moduleOn('attendance'))
                      _Tile(
                        icon: Icons.insights_outlined,
                        label: 'Attendance dashboard',
                        onTap: () => context.go('/more/admin/attendance'),
                      ),
                    if (canManageCases && moduleOn('helpdesk'))
                      _Tile(
                        icon: Icons.support_agent_outlined,
                        label: 'Helpdesk queue',
                        onTap: () => context.go('/more/admin/cases'),
                      ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 12),
            SectionCard(
              title: 'Documents & pay',
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
              child: Column(
                children: [
                  if (payrollModuleOn)
                    _Tile(
                      icon: Icons.receipt_long_outlined,
                      label: Strings.payslips,
                      onTap: () => context.go('/more/payslips'),
                    ),
                  _Tile(
                    icon: Icons.folder_copy_outlined,
                    label: Strings.documents,
                    onTap: () => context.go('/more/documents'),
                  ),
                  _Tile(
                    icon: Icons.policy_outlined,
                    label: Strings.policies,
                    onTap: () => context.go('/more/policies'),
                  ),
                  _Tile(
                    icon: Icons.description_outlined,
                    label: Strings.letters,
                    onTap: () => context.go('/more/letters'),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionCard(
              title: 'Claims & assets',
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
              child: Column(
                children: [
                  _Tile(
                    icon: Icons.receipt_outlined,
                    label: Strings.expenses,
                    onTap: () => context.go('/more/expenses'),
                  ),
                  _Tile(
                    icon: Icons.flight_takeoff_outlined,
                    label: Strings.travelRequests,
                    onTap: () => context.go('/more/expenses/travel'),
                  ),
                  _Tile(
                    icon: Icons.laptop_mac_outlined,
                    label: Strings.assets,
                    onTap: () => context.go('/more/assets'),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionCard(
              title: 'Grow & get help',
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
              child: Column(
                children: [
                  _Tile(
                    icon: Icons.trending_up_rounded,
                    label: Strings.performance,
                    onTap: () => context.go('/more/performance'),
                  ),
                  _Tile(
                    icon: Icons.support_agent_outlined,
                    label: Strings.helpdesk,
                    onTap: () => context.go('/more/cases'),
                  ),
                  _Tile(
                    icon: Icons.menu_book_outlined,
                    label: Strings.faq,
                    onTap: () => context.go('/more/cases/faq'),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionCard(
              title: 'News & settings',
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
              child: Column(
                children: [
                  _Tile(
                    icon: Icons.campaign_outlined,
                    label: Strings.announcements,
                    onTap: () => context.go('/home/announcements'),
                  ),
                  _Tile(
                    icon: Icons.notifications_outlined,
                    label: Strings.notifications,
                    onTap: () => context.go('/home/notifications'),
                  ),
                  _Tile(
                    icon: Icons.tune_rounded,
                    label: Strings.notificationPreferences,
                    onTap: () => context.go('/more/notifications-settings'),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 20),
            OutlinedButton.icon(
              onPressed: () => _signOut(context, ref),
              icon: const Icon(Icons.logout_rounded, size: 18),
              label: const Text(Strings.signOut),
              style: OutlinedButton.styleFrom(
                foregroundColor: scheme.error,
                side: BorderSide(color: scheme.error),
              ),
            ),
            const SizedBox(height: 24),
          ],
        ),
      ),
    );
  }
}

class _Tile extends StatelessWidget {
  const _Tile({required this.icon, required this.label, required this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      dense: true,
      contentPadding: EdgeInsets.zero,
      leading: Icon(icon),
      title: Text(label),
      trailing: const Icon(Icons.chevron_right_rounded, size: 20),
      onTap: onTap,
    );
  }
}
