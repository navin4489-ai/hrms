/// Maps API `deepLink` values (contract §5 dashboard / §16 notifications) to
/// in-app go_router locations.
///
/// Contract examples: `/leave/requests/{id}`, `/approvals/{taskId}`.
/// Routes defined inside the shell carry their branch prefix, so this parser
/// translates between the two.
class DeepLinkParser {
  const DeepLinkParser._();

  /// Returns a routable app location for an API deep link, or null when the
  /// link cannot be mapped (caller should fall back to the dashboard).
  static String? toAppRoute(String? deepLink) {
    if (deepLink == null || deepLink.trim().isEmpty) return null;
    var link = deepLink.trim();
    if (!link.startsWith('/')) link = '/$link';

    // Exact-match routes that already mirror the API paths.
    const direct = <String, String>{
      '/leave/requests': '/leave/requests', // handled with id below
      '/approvals': '/approvals',
      '/attendance': '/attendance',
      '/notifications': '/home/notifications',
      '/dashboard': '/home',
      '/home': '/home',
    };

    // /leave/requests/{id}
    final leaveMatch = RegExp(r'^/leave/requests/([^/]+)$').firstMatch(link);
    if (leaveMatch != null) {
      return '/leave/requests/${leaveMatch.group(1)}';
    }

    // /approvals/{taskId}
    final approvalMatch = RegExp(r'^/approvals/([^/]+)$').firstMatch(link);
    if (approvalMatch != null) {
      return '/approvals/${approvalMatch.group(1)}';
    }

    // /attendance-corrections/{id}
    final correctionMatch = RegExp(r'^/attendance[-/]corrections/([^/]+)$')
        .firstMatch(link);
    if (correctionMatch != null) {
      return '/attendance/corrections/${correctionMatch.group(1)}';
    }

    // /announcements/{id}
    final announcementMatch = RegExp(r'^/announcements/([^/]+)$')
        .firstMatch(link);
    if (announcementMatch != null) {
      return '/home/announcements/${announcementMatch.group(1)}';
    }

    // /payslips/{id}
    final payslipMatch = RegExp(r'^/payslips/([^/]+)$').firstMatch(link);
    if (payslipMatch != null) {
      return '/more/payslips/${payslipMatch.group(1)}';
    }

    // /cases/{id}
    final caseMatch = RegExp(r'^/cases/([^/]+)$').firstMatch(link);
    if (caseMatch != null) {
      final id = caseMatch.group(1);
      if (id == 'faq' || id == 'knowledge-base') return '/more/cases/faq';
      return '/more/cases/$id';
    }

    // /knowledge-base/{id}
    final kbMatch = RegExp(r'^/knowledge-base/([^/]+)$').firstMatch(link);
    if (kbMatch != null) {
      return '/more/cases/faq/${kbMatch.group(1)}';
    }

    // /expenses/{id}
    final expenseMatch = RegExp(r'^/expenses/([^/]+)$').firstMatch(link);
    if (expenseMatch != null) {
      return '/more/expenses/${expenseMatch.group(1)}';
    }

    // /employees/{id} -> directory detail
    final employeeMatch = RegExp(r'^/employees/([^/]+)$').firstMatch(link);
    if (employeeMatch != null) {
      return '/more/directory/${employeeMatch.group(1)}';
    }

    // /policies/{id}
    final policyMatch = RegExp(r'^/policies/([^/]+)$').firstMatch(link);
    if (policyMatch != null) {
      return '/more/policies/${policyMatch.group(1)}';
    }

    // Prefix/leaf routes.
    const leaf = <String, String>{
      '/announcements': '/home/announcements',
      '/payslips': '/more/payslips',
      '/documents': '/more/documents',
      '/policies': '/more/policies',
      '/letters': '/more/letters',
      '/expenses': '/more/expenses',
      '/travel-requests': '/more/expenses/travel',
      '/assets': '/more/assets',
      '/performance': '/more/performance',
      '/training': '/more/performance',
      '/cases': '/more/cases',
      '/knowledge-base': '/more/cases/faq',
      '/directory': '/more/directory',
      '/profile': '/more/profile',
      '/settings': '/more/notifications-settings',
      '/notifications-settings': '/more/notifications-settings',
      '/leave': '/leave',
      '/leave/calendar': '/leave/calendar',
      '/leave/team': '/leave/team',
      '/attendance-corrections': '/attendance/corrections',
    };

    if (leaf.containsKey(link)) return leaf[link];
    if (direct.containsKey(link)) {
      final mapped = direct[link]!;
      return mapped == '/leave/requests' ? null : mapped;
    }
    return null;
  }
}
