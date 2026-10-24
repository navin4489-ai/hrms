/// All user-facing English strings, centralized for future localization.
abstract final class Strings {
  // App
  static const appName = 'Wintech HRMS';

  // Common actions
  static const retry = 'Retry';
  static const cancel = 'Cancel';
  static const save = 'Save';
  static const submit = 'Submit';
  static const confirm = 'Confirm';
  static const close = 'Close';
  static const back = 'Back';
  static const search = 'Search';
  static const filter = 'Filter';
  static const refresh = 'Refresh';
  static const all = 'All';
  static const none = 'None';
  static const yes = 'Yes';
  static const no = 'No';
  static const optional = 'optional';
  static const required = 'Required';
  static const loadMore = 'Load more';
  static const copied = 'Copied';

  // Auth
  static const signIn = 'Sign in';
  static const signingIn = 'Signing in…';
  static const signOut = 'Sign out';
  static const username = 'Username, work email or employee number';
  static const usernameLabel = 'Username';
  static const password = 'Password';
  static const forgotPassword = 'Forgot password?';
  static const resetPassword = 'Reset password';
  static const sendResetLink = 'Send reset link';
  static const newPassword = 'New password';
  static const currentPassword = 'Current password';
  static const confirmPassword = 'Confirm new password';
  static const changePassword = 'Change password';
  static const invalidCredentials =
      'Incorrect username or password. Please try again.';
  static const accountNotActiveTitle = 'Account not active';
  static const accountNotActiveDetail =
      'Your account has been suspended or terminated. Contact HR if you believe this is a mistake.';
  static const loginCredentialHelp =
      'Use the username and password provided by your HRMS administrator.';
  static const mfaTitle = 'Two-factor verification';
  static const mfaSubtitle =
      'Enter the 6-digit code from your authenticator app.';
  static const mfaCodeLabel = 'Verification code';
  static const mfaVerify = 'Verify';
  static const mfaInvalidCode = 'The verification code is invalid or expired.';
  static const resetSent =
      'If that username exists, a reset link has been sent. Check your inbox.';
  static const resetLinkDevNote =
      'In development the reset token is logged by the API.';
  static const sessionExpired =
      'Your session has expired. Please sign in again.';
  static const loggedOut = 'You have been signed out.';
  static const logoutTitle = 'Sign out?';
  static const logoutMessage = 'You will need to sign in again to use the app.';

  // Navigation / tabs
  static const tabHome = 'Home';
  static const tabAttendance = 'Attendance';
  static const tabLeave = 'Leave';
  static const tabApprovals = 'Approvals';
  static const tabMore = 'More';

  // Generic states
  static const loading = 'Loading…';
  static const somethingWentWrong = 'Something went wrong';
  static const emptyTitle = 'Nothing here yet';
  static const emptyGeneric =
      'There is nothing to show right now. Pull down to refresh.';
  static const offlineTitle = 'No connection';
  static const offlineMessage =
      'Could not reach the server. Check your connection and try again.';
  static const noResults = 'No results found';
  static const noResultsHint = 'Try a different search term or clear filters.';
  static const savedSuccessfully = 'Saved successfully';
  static const actionCompleted = 'Done';

  // Dashboard
  static const dashboard = 'Dashboard';
  static const todayAttendance = "Today's attendance";
  static const punchIn = 'Punch in';
  static const punchOut = 'Punch out';
  static const punching = 'Punching…';
  static const quickActions = 'Quick actions';
  static const leaveBalances = 'Leave balances';
  static const pendingApprovals = 'Pending approvals';
  static const myRequests = 'My requests';
  static const announcements = 'Announcements';
  static const upcomingHolidays = 'Upcoming holidays';
  static const notifications = 'Notifications';
  static const notMarked = 'Not marked yet';
  static const shiftLabel = 'Shift';
  static const checkInLabel = 'Check-in';
  static const checkOutLabel = 'Check-out';
  static const workedHours = 'Worked';
  static const viewAll = 'View all';

  // Attendance
  static const attendanceTitle = 'Attendance';
  static const monthSummary = 'Monthly summary';
  static const legend = 'Legend';
  static const correctionRequest = 'Correction request';
  static const newCorrection = 'Request correction';
  static const myCorrections = 'My corrections';
  static const correctionType = 'Correction type';
  static const proposedCheckIn = 'Proposed check-in';
  static const proposedCheckOut = 'Proposed check-out';
  static const originalValues = 'Original values';
  static const reasonLabel = 'Reason';
  static const reasonRequired = 'Please provide a reason';
  static const workDate = 'Work date';
  static const noCorrections = 'No correction requests yet';
  static const noCorrectionsHint =
      'If your attendance has a missing or wrong punch, request a correction here.';
  static const dayDetailTitle = 'Day detail';

  // Leave
  static const leaveTitle = 'Leave';
  static const balances = 'Balances';
  static const myLeaveRequests = 'My requests';
  static const newLeaveRequest = 'New leave request';
  static const leaveType = 'Leave type';
  static const startDate = 'Start date';
  static const endDate = 'End date';
  static const dayType = 'Day type';
  static const daysEstimated = 'Estimated days';
  static const reason = 'Reason';
  static const attachmentUrl = 'Attachment URL';
  static const cancelRequest = 'Cancel request';
  static const cancelRequestConfirm =
      'This will withdraw your leave request. Continue?';
  static const leaveCalendar = 'Leave calendar';
  static const teamRequests = 'Team requests';
  static const noLeaveRequests = 'No leave requests';
  static const noLeaveRequestsHint =
      'Requests you submit will appear here with their approval status.';
  static const selectLeaveType = 'Select a leave type';
  static const selectDates = 'Select start and end dates';
  static const reasonTooShort =
      'Please provide a reason (at least 5 characters)';

  // Approvals
  static const approvalsTitle = 'Approvals';
  static const pending = 'Pending';
  static const decisionHistory = 'Decision history';
  static const approve = 'Approve';
  static const reject = 'Reject';
  static const returnRequest = 'Return';
  static const commentLabel = 'Comment';
  static const rejectCommentRequired = 'A comment is required to reject';
  static const returnCommentRequired = 'A comment is required to return';
  static const noPendingApprovals = 'All caught up';
  static const noPendingApprovalsHint =
      'There are no requests waiting for your decision.';
  static const myWorkflow = 'My requests';
  static const requester = 'Requester';
  static const requestedAt = 'Requested';
  static const dueBy = 'Due by';
  static const stepLabel = 'Step';
  static const notYourTask = 'You cannot decide on this request.';
  static const alreadyDecided = 'This request has already been decided.';
  static const decisionDone = 'Decision recorded';
  static const overdueLabel = 'Overdue';
  static const completedThisMonth = 'Completed this month';

  // Directory
  static const directory = 'Directory';
  static const department = 'Department';
  static const location = 'Location';
  static const designation = 'Designation';
  static const manager = 'Manager';
  static const workEmail = 'Work email';
  static const phone = 'Phone';
  static const noPhone = 'Not shared';
  static const employeeDetail = 'Employee';
  static const noEmployees = 'No employees found';
  static const addEmployee = 'Add employee';
  static const createEmployee = 'Create employee';
  static const employeeCreated = 'Employee added successfully';
  static const saving = 'Saving…';
  static const employeeNumberOptional = 'Employee number (optional)';
  static const firstName = 'First name';
  static const lastName = 'Last name';
  static const selectDepartment = 'Select a department';
  static const selectDesignation = 'Select a designation';
  static const selectLocation = 'Select a location';
  static const selectLegalEntity = 'Select a legal entity';

  // Profile
  static const profile = 'My profile';
  static const editProfile = 'Edit profile';
  static const preferredName = 'Preferred name';
  static const personalEmail = 'Personal email';
  static const address = 'Address';
  static const emergencyContact = 'Emergency contact';
  static const emergencyContactName = 'Emergency contact name';
  static const emergencyContactPhone = 'Emergency contact phone';
  static const employmentHistory = 'Employment history';
  static const sessions = 'Active sessions';
  static const revokeSession = 'Revoke';
  static const revokeSessionConfirm =
      'Revoke this session? That device will need to sign in again.';
  static const sensitiveChange = 'Request a sensitive change';
  static const sensitiveChangeHint =
      'Bank details, legal name and national ID changes are verified by HR before they take effect.';
  static const sensitiveField = 'Field';
  static const requestedValue = 'New value';
  static const changeReason = 'Why do you need this change?';
  static const sensitiveSubmitted =
      'Change request submitted for HR verification.';
  static const joiningDate = 'Joining date';
  static const employmentType = 'Employment type';
  static const employmentStatus = 'Status';
  static const legalEntity = 'Legal entity';
  static const costCentre = 'Cost centre';
  static const dateOfBirth = 'Date of birth';
  static const gender = 'Gender';
  static const thisDevice = 'This device';

  // Documents & policies
  static const payslips = 'Payslips';
  static const documents = 'Documents';
  static const policies = 'Policies';
  static const letters = 'Letters';
  static const download = 'Download';
  static const downloadedTo = 'Saved to';
  static const acknowledge = 'Acknowledge';
  static const acknowledged = 'Acknowledged';
  static const gross = 'Gross';
  static const deductions = 'Deductions';
  static const netPay = 'Net pay';
  static const earnings = 'Earnings';
  static const totalDeductions = 'Total deductions';
  static const period = 'Period';
  static const requestLetter = 'Request a letter';
  static const letterType = 'Letter type';
  static const purpose = 'Purpose';
  static const uploadDocument = 'Upload document';
  static const category = 'Category';
  static const documentType = 'Document type';
  static const issuedOn = 'Issued on';
  static const expiresOn = 'Expires on';
  static const chooseFile = 'Choose file';
  static const noPayslips = 'No payslips published yet';
  static const noPayslipsHint =
      'Published payslips appear here after payroll is released.';
  static const noDocuments = 'No documents on file';
  static const noPolicies = 'No policies published';
  static const noLetters = 'No letter requests yet';
  static const noLettersHint =
      'Request employment, salary or experience letters here.';
  static const policyAcknowledged = 'Policy acknowledged';
  static const policyAlreadyAcknowledged =
      'This version is already acknowledged.';
  static const acknowledgeConfirm =
      'Acknowledge that you have read this policy version?';

  // Expenses
  static const expenses = 'Expenses';
  static const travelRequests = 'Travel requests';
  static const newExpense = 'New expense';
  static const newTravelRequest = 'New travel request';
  static const amount = 'Amount';
  static const currency = 'Currency';
  static const expenseDate = 'Expense date';
  static const businessPurpose = 'Business purpose';
  static const costCentreLabel = 'Cost centre';
  static const receipt = 'Receipt';
  static const receiptOptional = 'Receipt (optional)';
  static const destination = 'Destination';
  static const estimatedCost = 'Estimated cost';
  static const noExpenses = 'No expenses yet';
  static const noExpensesHint =
      'Claims you submit will appear here with their approval status.';
  static const noTravel = 'No travel requests yet';
  static const expenseCategory = 'Expense category';

  // Assets
  static const assets = 'My assets';
  static const assetTag = 'Asset tag';
  static const serialNumber = 'Serial number';
  static const issuedOnLabel = 'Issued';
  static const condition = 'Condition';
  static const conditionNote = 'Note';
  static const returnDueDate = 'Return due';
  static const acknowledgeAsset = 'Acknowledge receipt';
  static const requestAssetReturn = 'Request return';
  static const returnReason = 'Reason for return';
  static const noAssets = 'No assets assigned';
  static const noAssetsHint = 'Equipment issued to you will show up here.';
  static const assetAcknowledged = 'Asset acknowledged';
  static const assetReturnRequested = 'Return request submitted';

  // Performance & training
  static const performance = 'Performance';
  static const training = 'Training';
  static const goals = 'Goals';
  static const myGoals = 'My goals';
  static const selfAssessment = 'Self-assessment';
  static const reviews = 'Reviews';
  static const courses = 'Courses';
  static const progress = 'Progress';
  static const weight = 'Weight';
  static const target = 'Target';
  static const rating = 'Rating';
  static const summary = 'Summary';
  static const currentCycle = 'Current cycle';
  static const assessmentContent = 'Your self-assessment';
  static const evidence = 'Evidence / links';
  static const submitAssessment = 'Submit assessment';
  static const assessmentSubmitted = 'Self-assessment submitted';
  static const acknowledgeReview = 'Acknowledge';
  static const reviewAcknowledged = 'Review acknowledged';
  static const enroll = 'Enrol';
  static const completeCourse = 'Mark complete';
  static const scoreLabel = 'Score';
  static const enrolled = 'Enrolled';
  static const completed = 'Completed';
  static const notEnrolled = 'Not enrolled';
  static const noGoals = 'No goals assigned';
  static const noReviews = 'No published reviews';
  static const noCourses = 'No courses assigned';
  static const mandatory = 'Mandatory';
  static const dueDate = 'Due';

  // Helpdesk
  static const helpdesk = 'Helpdesk';
  static const myCases = 'My cases';
  static const newCase = 'New case';
  static const caseDetail = 'Case';
  static const subject = 'Subject';
  static const description = 'Description';
  static const urgency = 'Urgency';
  static const subcategory = 'Subcategory';
  static const ticketNumber = 'Ticket';
  static const replies = 'Replies';
  static const replyPlaceholder = 'Write a reply…';
  static const send = 'Send';
  static const resolve = 'Resolve';
  static const reopen = 'Reopen';
  static const rateCase = 'Rate resolution';
  static const ratingComment = 'Comment (optional)';
  static const faq = 'FAQ / Knowledge base';
  static const noCases = 'No cases yet';
  static const noCasesHint =
      'Need help from HR? Create a case and track its progress here.';
  static const noFaq = 'No articles found';
  static const caseCreated = 'Case created';
  static const replySent = 'Reply sent';
  static const priority = 'Priority';

  // Announcements
  static const announcementsTitle = 'Announcements';
  static const noAnnouncements = 'No announcements';
  static const noAnnouncementsHint =
      'Company news and policy updates will appear here.';
  static const pinned = 'Pinned';
  static const announcementAcknowledged = 'Announcement acknowledged';

  // Notifications
  static const notificationsTitle = 'Notifications';
  static const unreadOnly = 'Unread only';
  static const markAllRead = 'Mark all read';
  static const markRead = 'Mark as read';
  static const noNotifications = 'No notifications';
  static const noNotificationsHint =
      'Updates about your requests, approvals and payroll will show up here.';
  static const allRead = 'All notifications marked as read';

  // Settings
  static const settings = 'Settings';
  static const notificationPreferences = 'Notification preferences';
  static const mandatoryCategoryHint =
      'Mandatory operational and security notices cannot be turned off.';
  static const channelInApp = 'In-app';
  static const channelPush = 'Push';
  static const channelEmail = 'Email';
  static const preferencesSaved = 'Preferences saved';
  static const preferencesMandatoryError =
      'Mandatory categories cannot be disabled.';
  static const account = 'Account';
  static const logout = 'Sign out';

  // More hub
  static const moreTitle = 'More';

  // Errors mapped to fields
  static const fieldRequired = 'This field is required';
  static const invalidEmail = 'Enter a valid email address';
  static const invalidPhone = 'Enter a valid phone number';
  static const passwordTooShort = 'Password must be at least 8 characters';
  static const passwordMismatch = 'Passwords do not match';
  static const dateOrderError = 'End date must be on or after start date';
}
