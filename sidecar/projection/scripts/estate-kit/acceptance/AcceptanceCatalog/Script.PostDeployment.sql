/*
  Post-deployment — includes the seed. The seed is additive (MERGE without a
  delete arm) so republishing never prunes rows behind the acceptance's back.
*/
:r .\Data\Seed.sql
