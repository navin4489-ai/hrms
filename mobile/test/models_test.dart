import 'package:flutter_test/flutter_test.dart';

import 'package:wintech_hrms/models/case_models.dart';
import 'package:wintech_hrms/models/communication_models.dart';
import 'package:wintech_hrms/models/auth_models.dart';
import 'package:wintech_hrms/models/me_models.dart';

void main() {
  group('UserPrincipal permissions', () {
    test('SuperAdmin wildcard grants every mobile permission', () {
      const user = UserPrincipal(
        id: 'u1',
        employeeId: 'e1',
        employeeNumber: 'EMP001',
        displayName: 'Admin',
        permissions: ['*'],
      );

      expect(user.hasPermission('employee.create'), isTrue);
      expect(user.hasPermission('admin.users'), isTrue);
    });

    test('regular permissions stay limited to the granted codes', () {
      const user = UserPrincipal(
        id: 'u2',
        employeeId: 'e2',
        employeeNumber: 'EMP002',
        displayName: 'Employee',
        permissions: ['employee.view'],
      );

      expect(user.hasPermission('employee.view'), isTrue);
      expect(user.hasPermission('employee.create'), isFalse);
    });
  });

  group('Announcement.fromJson (contract §16)', () {
    test('parses every documented field', () {
      final item = Announcement.fromJson(const {
        'id': 'a1',
        'title': 'Office closed',
        'summary': 'Building shut on Friday',
        'body': 'Full details here.',
        'category': 'Company',
        'audience': 'All',
        'pinned': true,
        'publishedAt': '2026-01-05T09:00:00Z',
        'expiresAt': '2026-02-05T09:00:00Z',
        'requiresAcknowledgement': true,
        'acknowledgedAt': null,
      });

      expect(item.id, 'a1');
      expect(item.title, 'Office closed');
      expect(item.category, 'Company');
      expect(item.audience, 'All');
      expect(item.pinned, isTrue);
      expect(item.requiresAcknowledgement, isTrue);
      expect(item.acknowledgedAt, isNull);
      expect(item.needsAcknowledgement, isTrue);
    });

    test('missing optional fields fall back to safe defaults', () {
      final item = Announcement.fromJson(const {'id': 'a2', 'title': 'T'});
      expect(item.pinned, isFalse);
      expect(item.requiresAcknowledgement, isFalse);
      expect(item.needsAcknowledgement, isFalse);
      expect(item.summary, isNull);
    });

    test('acknowledged announcement no longer needs acknowledgement', () {
      final item = Announcement.fromJson(const {
        'id': 'a3',
        'title': 'T',
        'requiresAcknowledgement': true,
        'acknowledgedAt': '2026-01-06T10:00:00Z',
      });
      expect(item.needsAcknowledgement, isFalse);
    });
  });

  group('NotificationItem.fromJson (FR-EMP-122)', () {
    test('parses the documented envelope', () {
      final item = NotificationItem.fromJson(const {
        'id': 'n1',
        'type': 'leave.approved',
        'title': 'Leave approved',
        'body': 'Your leave was approved.',
        'deepLink': '/leave/requests/r-9',
        'category': 'Leave',
        'createdAt': '2026-01-07T08:30:00Z',
        'readAt': null,
      });

      expect(item.id, 'n1');
      expect(item.type, 'leave.approved');
      expect(item.deepLink, '/leave/requests/r-9');
      expect(item.isRead, isFalse);
    });

    test('readAt marks the item read', () {
      final item = NotificationItem.fromJson(const {
        'id': 'n2',
        'title': 'Payslip ready',
        'readAt': '2026-01-08T12:00:00Z',
      });
      expect(item.isRead, isTrue);
      expect(item.body, isNull);
    });
  });

  group('UnreadCount.fromJson', () {
    test('parses count and defaults to zero', () {
      expect(UnreadCount.fromJson(const {'count': 7}).count, 7);
      expect(UnreadCount.fromJson(const {}).count, 0);
    });
  });

  group('NotificationPreference (FR-EMP-121)', () {
    test('fromJson -> toJson round-trips', () {
      const source = {
        'category': 'Payroll',
        'inApp': true,
        'push': false,
        'email': true,
        'mandatory': false,
      };
      final pref = NotificationPreference.fromJson(source);
      expect(pref.category, 'Payroll');
      expect(pref.inApp, isTrue);
      expect(pref.push, isFalse);
      expect(pref.email, isTrue);
      expect(pref.toJson(), source);
    });

    test('copyWith keeps category and mandatory flag', () {
      final pref = NotificationPreference.fromJson(const {
        'category': 'Security',
        'mandatory': true,
      });
      final changed = pref.copyWith(push: false);
      expect(changed.category, 'Security');
      expect(changed.mandatory, isTrue);
      expect(changed.push, isFalse);
      expect(pref.push, isTrue); // original untouched
    });
  });

  group('KbArticle.fromJson', () {
    test('parses FAQ article fields', () {
      final article = KbArticle.fromJson(const {
        'id': 'kb1',
        'title': 'How to punch in',
        'summary': 'Attendance basics',
        'category': 'Attendance',
        'tags': ['punch', 'attendance'],
        'helpfulCount': 12,
        'updatedAt': '2026-01-02T06:00:00Z',
      });
      expect(article.id, 'kb1');
      expect(article.category, 'Attendance');
      expect(article.tags, ['punch', 'attendance']);
      expect(article.helpfulCount, 12);
    });

    test('tolerates a payload without optional fields', () {
      final article = KbArticle.fromJson(const {'id': 'kb2', 'title': 'FAQ'});
      expect(article.tags, isEmpty);
      expect(article.helpfulCount, 0);
      expect(article.updatedAt, isNull);
    });
  });

  group('KbArticleDetail.fromJson', () {
    test('parses detail payload', () {
      final detail = KbArticleDetail.fromJson(const {
        'id': 'kb1',
        'title': 'How to punch in',
        'body': 'Open the app and tap Punch in.',
        'category': 'Attendance',
        'version': '2.1',
        'expiresAt': '2026-12-31T00:00:00Z',
      });
      expect(detail.version, '2.1');
      expect(detail.body, contains('Punch in'));
      expect(detail.expiresAt, '2026-12-31T00:00:00Z');
    });
  });
}
