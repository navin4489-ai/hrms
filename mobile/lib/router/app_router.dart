import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/announcements/announcement_detail_screen.dart';
import '../features/announcements/announcements_screen.dart';
import '../features/approvals/approval_detail_screen.dart';
import '../features/approvals/approvals_screen.dart';
import '../features/attendance/attendance_screen.dart';
import '../features/attendance/correction_detail_screen.dart';
import '../features/attendance/correction_form_screen.dart';
import '../features/attendance/corrections_screen.dart';
import '../features/auth/account_blocked_screen.dart';
import '../features/auth/forgot_password_screen.dart';
import '../features/auth/login_screen.dart';
import '../features/auth/mfa_screen.dart';
import '../features/auth/splash_screen.dart';
import '../features/dashboard/dashboard_screen.dart';
import '../features/directory/directory_screen.dart';
import '../features/directory/create_employee_screen.dart';
import '../features/directory/employee_detail_screen.dart';
import '../features/documents/documents_screen.dart';
import '../features/documents/letters_screen.dart';
import '../features/documents/payslip_detail_screen.dart';
import '../features/documents/payslips_screen.dart';
import '../features/documents/policies_screen.dart';
import '../features/documents/policy_detail_screen.dart';
import '../features/expenses/expense_detail_screen.dart';
import '../features/expenses/expenses_screen.dart';
import '../features/expenses/new_expense_screen.dart';
import '../features/expenses/new_travel_screen.dart';
import '../features/expenses/travel_screen.dart';
import '../features/helpdesk/case_detail_screen.dart';
import '../features/helpdesk/cases_screen.dart';
import '../features/helpdesk/kb_article_screen.dart';
import '../features/helpdesk/knowledge_base_screen.dart';
import '../features/helpdesk/new_case_screen.dart';
import '../features/leave/leave_calendar_screen.dart';
import '../features/leave/leave_request_detail_screen.dart';
import '../features/leave/leave_screen.dart';
import '../features/leave/new_leave_screen.dart';
import '../features/leave/team_leave_screen.dart';
import '../features/more/more_screen.dart';
import '../features/notifications/notifications_screen.dart';
import '../features/profile/change_password_screen.dart';
import '../features/profile/edit_profile_screen.dart';
import '../features/profile/employment_history_screen.dart';
import '../features/profile/profile_screen.dart';
import '../features/profile/sensitive_change_screen.dart';
import '../features/profile/sessions_screen.dart';
import '../features/settings/notification_preferences_screen.dart';
import '../features/assets/assets_screen.dart';
import '../features/performance/performance_screen.dart';
import '../features/performance/self_assessment_screen.dart';
import '../providers/auth_provider.dart';
import '../features/shell/app_shell.dart';

/// Auth routes (accessible without a session).
const _authPaths = <String>{'/login', '/mfa', '/forgot-password', '/blocked'};

/// Central go_router configuration with auth redirect + deep-link support.
final routerProvider = Provider<GoRouter>((ref) {
  // Recreate the router when the session status flips (login/logout/expiry).
  final status = ref.watch(authControllerProvider.select((s) => s.status));

  final router = GoRouter(
    initialLocation: '/splash',
    redirect: (context, state) {
      final path = state.matchedLocation;
      switch (status) {
        case AuthStatus.restoring:
          return path == '/splash' ? null : '/splash';
        case AuthStatus.unauthenticated:
          if (path == '/login' || path == '/forgot-password') return null;
          return '/login';
        case AuthStatus.mfaRequired:
          return path == '/mfa' ? null : '/mfa';
        case AuthStatus.blocked:
          return path == '/blocked' ? null : '/blocked';
        case AuthStatus.authenticated:
          if (_authPaths.contains(path) || path == '/splash' || path == '/') {
            return '/home';
          }
          return null;
      }
    },
    errorBuilder: (context, state) => NotFoundScreen(location: state.uri.path),
    routes: [
      GoRoute(path: '/splash', builder: (_, __) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/mfa', builder: (_, __) => const MfaScreen()),
      GoRoute(
        path: '/forgot-password',
        builder: (_, __) => const ForgotPasswordScreen(),
      ),
      GoRoute(
        path: '/blocked',
        builder: (_, __) => const AccountBlockedScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            AppShell(shell: navigationShell),
        branches: [
          // ---------------------------------------------------------------- home
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/home',
                builder: (_, __) => const DashboardScreen(),
                routes: [
                  GoRoute(
                    path: 'notifications',
                    builder: (_, __) => const NotificationsScreen(),
                  ),
                  GoRoute(
                    path: 'announcements',
                    builder: (_, __) => const AnnouncementsScreen(),
                    routes: [
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => AnnouncementDetailScreen(
                          announcementId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ],
          ),
          // ---------------------------------------------------------- attendance
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/attendance',
                builder: (_, __) => const AttendanceScreen(),
                routes: [
                  GoRoute(
                    path: 'corrections',
                    builder: (_, __) => const CorrectionsScreen(),
                    routes: [
                      GoRoute(
                        path: 'new',
                        builder: (_, __) => const CorrectionFormScreen(),
                      ),
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => CorrectionDetailScreen(
                          correctionId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ],
          ),
          // --------------------------------------------------------------- leave
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/leave',
                builder: (_, __) => const LeaveScreen(),
                routes: [
                  GoRoute(
                    path: 'new',
                    builder: (_, __) => const NewLeaveScreen(),
                  ),
                  GoRoute(
                    path: 'requests/:id',
                    builder: (_, state) => LeaveRequestDetailScreen(
                      requestId: state.pathParameters['id']!,
                    ),
                  ),
                  GoRoute(
                    path: 'calendar',
                    builder: (_, __) => const LeaveCalendarScreen(),
                  ),
                  GoRoute(
                    path: 'team',
                    builder: (_, __) => const TeamLeaveScreen(),
                  ),
                ],
              ),
            ],
          ),
          // ----------------------------------------------------------- approvals
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/approvals',
                builder: (_, __) => const ApprovalsScreen(),
                routes: [
                  GoRoute(
                    path: ':taskId',
                    builder: (_, state) => ApprovalDetailScreen(
                      taskId: state.pathParameters['taskId']!,
                    ),
                  ),
                ],
              ),
            ],
          ),
          // ---------------------------------------------------------------- more
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/more',
                builder: (_, __) => const MoreScreen(),
                routes: [
                  // Directory
                  GoRoute(
                    path: 'directory',
                    builder: (_, __) => const DirectoryScreen(),
                    routes: [
                      GoRoute(
                        path: 'new',
                        builder: (_, __) => const CreateEmployeeScreen(),
                      ),
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => EmployeeDetailScreen(
                          employeeId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                  // Profile (static children before ':…' patterns)
                  GoRoute(
                    path: 'profile',
                    builder: (_, __) => const ProfileScreen(),
                    routes: [
                      GoRoute(
                        path: 'edit',
                        builder: (_, __) => const EditProfileScreen(),
                      ),
                      GoRoute(
                        path: 'sensitive',
                        builder: (_, __) => const SensitiveChangeScreen(),
                      ),
                      GoRoute(
                        path: 'history',
                        builder: (_, __) => const EmploymentHistoryScreen(),
                      ),
                      GoRoute(
                        path: 'sessions',
                        builder: (_, __) => const SessionsScreen(),
                      ),
                      GoRoute(
                        path: 'change-password',
                        builder: (_, __) => const ChangePasswordScreen(),
                      ),
                    ],
                  ),
                  // Payslips
                  GoRoute(
                    path: 'payslips',
                    builder: (_, __) => const PayslipsScreen(),
                    routes: [
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => PayslipDetailScreen(
                          payslipId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                  // Documents & policies & letters
                  GoRoute(
                    path: 'documents',
                    builder: (_, __) => const DocumentsScreen(),
                  ),
                  GoRoute(
                    path: 'policies',
                    builder: (_, __) => const PoliciesScreen(),
                    routes: [
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => PolicyDetailScreen(
                          policyId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                  GoRoute(
                    path: 'letters',
                    builder: (_, __) => const LettersScreen(),
                  ),
                  // Expenses & travel
                  GoRoute(
                    path: 'expenses',
                    builder: (_, __) => const ExpensesScreen(),
                    routes: [
                      GoRoute(
                        path: 'new',
                        builder: (_, __) => const NewExpenseScreen(),
                      ),
                      GoRoute(
                        path: 'travel',
                        builder: (_, __) => const TravelScreen(),
                        routes: [
                          GoRoute(
                            path: 'new',
                            builder: (_, __) => const NewTravelScreen(),
                          ),
                        ],
                      ),
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => ExpenseDetailScreen(
                          expenseId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                  // Assets
                  GoRoute(
                    path: 'assets',
                    builder: (_, __) => const AssetsScreen(),
                  ),
                  // Performance & training
                  GoRoute(
                    path: 'performance',
                    builder: (_, __) => const PerformanceScreen(),
                    routes: [
                      GoRoute(
                        path: 'self-assessment',
                        builder: (_, state) => SelfAssessmentScreen(
                          cycleId: state.uri.queryParameters['cycleId'] ?? '',
                        ),
                      ),
                    ],
                  ),
                  // Helpdesk
                  GoRoute(
                    path: 'cases',
                    builder: (_, __) => const CasesScreen(),
                    routes: [
                      GoRoute(
                        path: 'new',
                        builder: (_, __) => const NewCaseScreen(),
                      ),
                      GoRoute(
                        path: 'faq',
                        builder: (_, __) => const KnowledgeBaseScreen(),
                        routes: [
                          GoRoute(
                            path: ':id',
                            builder: (_, state) => KbArticleScreen(
                              articleId: state.pathParameters['id']!,
                            ),
                          ),
                        ],
                      ),
                      GoRoute(
                        path: ':id',
                        builder: (_, state) => CaseDetailScreen(
                          caseId: state.pathParameters['id']!,
                        ),
                      ),
                    ],
                  ),
                  // Settings
                  GoRoute(
                    path: 'notifications-settings',
                    builder: (_, __) => const NotificationPreferencesScreen(),
                  ),
                ],
              ),
            ],
          ),
        ],
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

/// Simple 404 page for unmatched locations.
class NotFoundScreen extends StatelessWidget {
  const NotFoundScreen({super.key, required this.location});

  final String location;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Page not found')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text('No page exists for "$location".'),
        ),
      ),
    );
  }
}
